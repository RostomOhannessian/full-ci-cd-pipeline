using Catalog.Application.Messaging;
using Catalog.Application.Results;
using Catalog.Application.Security;
using Catalog.CrossCutting.Decorators;
using FluentValidation;

namespace Catalog.Application.Tests.Support;

/// <summary>A complete command pipeline over fakes that share one journal, composed by the same <see cref="CommandPipeline"/> that the composition root uses.</summary>
internal sealed class CommandRig
{
    public CommandRig(
        TestPrincipal? caller = null,
        Func<SampleCommand, Result<SampleView>>? respond = null,
        Exception? outboxFailure = null,
        Action<SampleCommandValidator>? extraRule = null)
    {
        Caller = caller ?? Callers.Admin;
        Handler = new FakeCommandHandler(Journal, respond);
        Telemetry = new FakeTelemetry(Journal);
        Store = new FakeIdempotencyStore(Journal);
        UnitOfWork = new FakeUnitOfWork(Journal);
        Outbox = new FakeOutbox(Journal, outboxFailure);
        Pipeline = CommandPipeline.Compose<SampleCommand, SampleView>(
            Handler,
            Telemetry,
            [new SampleCommandValidator(Journal, extraRule)],
            new RecordingPrincipal(Journal, Caller),
            Store,
            UnitOfWork,
            Outbox);
    }

    public Journal Journal { get; } = new();

    public TestPrincipal Caller { get; }

    public FakeCommandHandler Handler { get; }

    public FakeTelemetry Telemetry { get; }

    public FakeIdempotencyStore Store { get; }

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeOutbox Outbox { get; }

    public ICommandHandler<SampleCommand, SampleView> Pipeline { get; }
}

/// <summary>A complete query pipeline over fakes that share one journal, composed by the same <see cref="QueryPipeline"/> that the composition root uses.</summary>
internal sealed class QueryRig<TQuery>
    where TQuery : IProtectedRequest
{
    public QueryRig(TestPrincipal? caller = null, Func<TQuery, Result<SampleView>>? respond = null, Func<Journal, IEnumerable<IValidator<TQuery>>>? validators = null)
    {
        Caller = caller ?? Callers.Admin;
        Handler = new FakeQueryHandler<TQuery>(Journal, respond);
        Cache = new FakeCache(Journal);
        Pipeline = QueryPipeline.Compose<TQuery, SampleView>(
            Handler,
            new FakeTelemetry(Journal),
            validators?.Invoke(Journal) ?? [],
            new RecordingPrincipal(Journal, Caller),
            Cache);
    }

    public Journal Journal { get; } = new();

    public TestPrincipal Caller { get; }

    public FakeQueryHandler<TQuery> Handler { get; }

    public FakeCache Cache { get; }

    public IQueryHandler<TQuery, SampleView> Pipeline { get; }
}

/// <summary>Callers with every scope and one role each, which is what the realm gives the three demo users, plus the caller that Keycloak lets request a scope that its role does not grant.</summary>
internal static class Callers
{
    private static readonly string[] AllScopes = [PermissionPolicy.ReadScope, PermissionPolicy.WriteScope, PermissionPolicy.AdminScope];

    public static TestPrincipal Reader => TestPrincipal.With(AllScopes, [PermissionPolicy.ReaderRole]);

    public static TestPrincipal Editor => TestPrincipal.With(AllScopes, [PermissionPolicy.EditorRole]);

    public static TestPrincipal Admin => TestPrincipal.With(AllScopes, [PermissionPolicy.AdminRole]);

    public static TestPrincipal Anonymous => new() { IsAuthenticated = false, CallerId = string.Empty };

    /// <summary>Returns one of the callers that the authorization tests name.</summary>
    public static TestPrincipal Named(string name) => name switch
    {
        "reader" => Reader,
        "editor" => Editor,
        "admin" => Admin,
        "anonymous" => Anonymous,
        "write scope without a role" => TestPrincipal.With([PermissionPolicy.WriteScope], []),
        "editor role without the scope" => TestPrincipal.With([], [PermissionPolicy.EditorRole]),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "The test has no caller of that name."),
    };
}
