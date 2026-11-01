using Catalog.Application.Caching;
using Catalog.Application.Events;
using Catalog.Application.Idempotency;
using Catalog.Application.Messaging;
using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Application.Security;
using Catalog.Application.Telemetry;
using FluentValidation;

namespace Catalog.Application.Tests.Support;

/// <summary>The ordered record of what the fakes saw, so a test can assert the order of the decorators.</summary>
internal sealed class Journal
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry) => _entries.Add(entry);
}

/// <summary>A command with a key, for the tests of the pipeline. It needs the write level.</summary>
internal sealed record SampleCommand(string Name, IdempotencyKey? IdempotencyKey = null) : IProtectedRequest, IIdempotentCommand
{
    public static AccessLevel RequiredAccess => AccessLevel.Write;
}

/// <summary>A query that the cache keeps. It needs the read level.</summary>
internal sealed record SampleQuery(string Id) : IProtectedRequest, ICachedQuery
{
    public static AccessLevel RequiredAccess => AccessLevel.Read;

    public CacheKey CacheKey => new($"sample:{Id}", ["sample"]);
}

/// <summary>A query that the cache does not keep. It needs the admin level.</summary>
internal sealed record UncachedQuery(string Id) : IProtectedRequest
{
    public static AccessLevel RequiredAccess => AccessLevel.Admin;
}

/// <summary>The value that the sample use cases return. It round-trips through JSON, as the idempotency store needs.</summary>
internal sealed record SampleView(string Name);

internal sealed class FakeCommandHandler(Journal journal, Func<SampleCommand, Result<SampleView>>? respond = null) : ICommandHandler<SampleCommand, SampleView>
{
    public int Calls { get; private set; }

    public Task<Result<SampleView>> HandleAsync(SampleCommand command, CancellationToken cancellationToken)
    {
        Calls++;
        journal.Add("handler");

        return Task.FromResult(respond?.Invoke(command) ?? Result.Success(new SampleView(command.Name)));
    }
}

internal sealed class FakeQueryHandler<TQuery>(Journal journal, Func<TQuery, Result<SampleView>>? respond = null) : IQueryHandler<TQuery, SampleView>
{
    public int Calls { get; private set; }

    public Task<Result<SampleView>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        Calls++;
        journal.Add("handler");

        return Task.FromResult(respond?.Invoke(query) ?? Result.Success(new SampleView("loaded")));
    }
}

internal sealed class FakeTelemetry(Journal journal) : IUseCaseTelemetry
{
    public IUseCaseObservation Begin(string useCase, UseCaseKind kind)
    {
        journal.Add($"telemetry:begin {useCase} {kind}");

        return new Observation(journal);
    }

    private sealed class Observation(Journal journal) : IUseCaseObservation
    {
        public void Succeeded() => journal.Add("telemetry:succeeded");

        public void Refused(ApplicationError reason) => journal.Add($"telemetry:refused {reason.GetType().Name}");

        public void Faulted(Exception exception) => journal.Add($"telemetry:faulted {exception.GetType().Name}");

        public void Dispose() => journal.Add("telemetry:end");
    }
}

/// <summary>A validator that records that it ran and refuses an empty name. A test can add a second rule through the extra action of its constructor.</summary>
internal sealed class SampleCommandValidator : AbstractValidator<SampleCommand>
{
    public SampleCommandValidator(Journal journal, Action<SampleCommandValidator>? extra = null)
    {
        RuleFor(command => command.Name).Must(name =>
        {
            journal.Add("validation");
            return !string.IsNullOrWhiteSpace(name);
        }).WithMessage("The name is required.");

        extra?.Invoke(this);
    }
}

/// <summary>A validator of the cached sample query that records that it ran and refuses an empty identifier.</summary>
internal sealed class SampleQueryValidator : AbstractValidator<SampleQuery>
{
    public SampleQueryValidator(Journal journal) =>
        RuleFor(query => query.Id).Must(id =>
        {
            journal.Add("validation");
            return !string.IsNullOrWhiteSpace(id);
        }).WithMessage("The identifier is required.");
}

/// <summary>A caller that records the moment that the authorization decorator reads its scopes.</summary>
internal sealed class RecordingPrincipal(Journal journal, TestPrincipal inner) : ICurrentPrincipal
{
    public bool IsAuthenticated => inner.IsAuthenticated;

    public string CallerId => inner.CallerId;

    public IReadOnlySet<string> Scopes
    {
        get
        {
            journal.Add("authorization");
            return inner.Scopes;
        }
    }

    public IReadOnlySet<string> Roles => inner.Roles;
}

/// <summary>A unit of work that runs the work, the hooks, and the commit in the order that the real one does, and records each step.</summary>
internal sealed class FakeUnitOfWork(Journal journal) : IUnitOfWork
{
    private readonly List<Func<object, CancellationToken, Task>> _hooks = [];

    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        journal.Add("uow:save");

        return Task.CompletedTask;
    }

    public void BeforeCommit(Func<object, CancellationToken, Task> action) => _hooks.Add(action);

    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        journal.Add("uow:begin");
        var hooks = _hooks.ToList();
        _hooks.Clear();

        var result = await work(cancellationToken);

        if (!result.IsSuccess)
        {
            journal.Add("uow:rollback");
            return result;
        }

        journal.Add("uow:hooks");
        foreach (var hook in hooks)
        {
            await hook(result.Value!, cancellationToken);
        }

        journal.Add("uow:commit");

        return result;
    }
}

/// <summary>An idempotency store with the semantics of the contract: a key belongs to one caller and one fingerprint, a running request is in progress, and a finished one replays.</summary>
internal sealed class FakeIdempotencyStore(Journal journal) : IIdempotencyStore
{
    private readonly Dictionary<(string Caller, string Key), Entry> _entries = [];

    public IReadOnlyList<IdempotencyRequest> Requests => _requests;

    private readonly List<IdempotencyRequest> _requests = [];

    public string? StoredResponse(string caller, string key) => _entries.TryGetValue((caller, key), out var entry) ? entry.Response : null;

    public Task<IdempotencyDecision> BeginAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        journal.Add("idempotency:begin");
        _requests.Add(request);
        var id = (request.CallerId, request.Key.Value);

        if (!_entries.TryGetValue(id, out var entry))
        {
            _entries[id] = new Entry(request.Fingerprint, null);
            return Task.FromResult(new IdempotencyDecision(IdempotencyOutcome.Reserved));
        }

        var outcome = entry.Fingerprint != request.Fingerprint
            ? Decision(IdempotencyOutcome.Reused)
            : entry.Response is null ? Decision(IdempotencyOutcome.InProgress) : Decision(IdempotencyOutcome.Replay, entry.Response);

        return Task.FromResult(outcome);
    }

    public Task CompleteAsync(IdempotencyRequest request, string response, CancellationToken cancellationToken)
    {
        journal.Add("idempotency:complete");
        _entries[(request.CallerId, request.Key.Value)] = new Entry(request.Fingerprint, response);

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        journal.Add("idempotency:release");
        _entries.Remove((request.CallerId, request.Key.Value));

        return Task.CompletedTask;
    }

    private static IdempotencyDecision Decision(IdempotencyOutcome outcome, string? response = null) => new(outcome, response);

    private sealed record Entry(string Fingerprint, string? Response);
}

internal sealed class FakeOutbox(Journal journal, Exception? failure = null) : IOutboxDispatcher
{
    public Task DispatchCommittedAsync(CancellationToken cancellationToken)
    {
        journal.Add("outbox:dispatch");

        return failure is null ? Task.CompletedTask : Task.FromException(failure);
    }
}

/// <summary>A cache that keeps a success and passes a failure through, as the real adapter does.</summary>
internal sealed class FakeCache(Journal journal) : ICatalogCache
{
    private readonly Dictionary<string, object> _entries = [];

    public async Task<Result<T>> GetOrLoadAsync<T>(CacheKey key, Func<CancellationToken, Task<Result<T>>> load, CancellationToken cancellationToken)
    {
        journal.Add($"cache:get {key.Name}");

        if (_entries.TryGetValue(key.Name, out var hit))
        {
            return (Result<T>)hit;
        }

        var result = await load(cancellationToken);

        if (result.IsSuccess)
        {
            journal.Add("cache:store");
            _entries[key.Name] = result;
        }

        return result;
    }

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        journal.Add($"cache:remove {tag}");
        _entries.Clear();

        return Task.CompletedTask;
    }
}
