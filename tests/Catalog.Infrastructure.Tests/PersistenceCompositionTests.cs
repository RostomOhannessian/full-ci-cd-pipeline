using System.Threading.Channels;
using AwesomeAssertions;
using Catalog.Application.Categories;
using Catalog.Application.Events;
using Catalog.Application.Idempotency;
using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-002, REQ-DATA-003, REQ-DATA-004, and REQ-DATA-005: a real dependency injection container wires the persistence layer with the registrations that the Api builds in WP1.10, so the credential interceptor,
/// the repositories, the unit of work, the outbox interceptor, the processor loop, the dispatcher, and the idempotency store work together. A commit reaches a handler through the loop, the connection runs as the
/// login of the configured credential, and a setting that holds a credential is refused when the options are read, with a message that never repeats it.
/// </summary>
public sealed class PersistenceCompositionTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    [Trait("Requirement", "REQ-DATA-003")]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task A_commit_through_the_container_reaches_the_handler_through_the_loop_as_the_configured_login()
    {
        await database.ResetAsync();
        var login = await SqlLogins.CreateAsync(database, "composed");
        var arrivals = Channel.CreateUnbounded<OutboxMessageEnvelope>();
        var handler = new RecordingHandler { Behavior = (message, _) => arrivals.Writer.WriteAsync(message, Token).AsTask() };
        var services = new ServiceCollection();
        services.AddCatalogPersistence();
        services.Configure<DatabaseOptions>(options =>
        {
            options.ConnectionString = SqlLogins.BaseConnectionString(database);
            options.UserName = login.UserName;
            options.Password = login.Password;
        });
        services.AddSingleton<IOutboxMessageHandler>(handler);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
        {
            await service.StartAsync(Token);
        }

        try
        {
            var category = Make.Category(Counters.Next(), "Keyboards");
            await using (var scope = provider.CreateAsyncScope())
            {
                var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
                (await SqlLogins.LoginOfAsync(scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), Token)).Should().Be(login.UserName);

                var result = await unit.ExecuteAsync(
                    _ =>
                    {
                        categories.Add(category);

                        return Task.FromResult(Result.Success(1));
                    },
                    Token);
                await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchCommittedAsync(Token);

                result.IsSuccess.Should().BeTrue();
            }

            // The limit is a safety net: the channel ends the wait when the loop delivers the message.
            var message = await arrivals.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token);
            message.EventType.Should().Be("CategoryCreated");
            message.Payload.Should().Contain(category.Id.Value.ToString());

            await using var reading = provider.CreateAsyncScope();
            var view = await reading.ServiceProvider.GetRequiredService<ICategoryReadStore>().GetAsync(category.Id, Token);
            view.Should().NotBeNull();
            (await SqlLogins.LoginOfAsync(reading.ServiceProvider.GetRequiredService<CatalogDbContext>(), Token)).Should().Be(login.UserName);
        }
        finally
        {
            foreach (var service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task The_container_gives_each_scope_its_own_context_and_an_idempotency_store_that_uses_it()
    {
        await database.ResetAsync();
        var login = await SqlLogins.CreateAsync(database, "scoped");
        var services = new ServiceCollection();
        services.AddCatalogPersistence();
        services.Configure<DatabaseOptions>(options =>
        {
            options.ConnectionString = SqlLogins.BaseConnectionString(database);
            options.UserName = login.UserName;
            options.Password = login.Password;
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.True(IdempotencyKey.TryCreate("composed-key", out var key));
        var request = new IdempotencyRequest(key, "caller-a", new string('a', 64));

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var decision = await first.ServiceProvider.GetRequiredService<IIdempotencyStore>().BeginAsync(request, Token);
        var again = await second.ServiceProvider.GetRequiredService<IIdempotencyStore>().BeginAsync(request, Token);

        first.ServiceProvider.GetRequiredService<CatalogDbContext>().Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<CatalogDbContext>());
        first.ServiceProvider.GetRequiredService<CatalogDbContext>().Should().BeSameAs(first.ServiceProvider.GetRequiredService<CatalogDbContext>());
        decision.Outcome.Should().Be(IdempotencyOutcome.Reserved);
        again.Outcome.Should().Be(IdempotencyOutcome.InProgress);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_setting_that_holds_a_credential_is_refused_when_the_options_are_read_and_the_message_never_repeats_it()
    {
        var services = new ServiceCollection();
        services.AddCatalogPersistence();
        services.Configure<DatabaseOptions>(options =>
        {
            options.ConnectionString = "Server=x;Database=catalog;User ID=sa;Password=Pw-Secret-test";
            options.UserName = "login_a";
            options.Password = "Pw-Secret-test";
        });
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        var failure = read.Should().Throw<OptionsValidationException>().Which;
        failure.Message.Should().Contain("Password").And.NotContain("Pw-Secret");
        failure.ToString().Should().NotContain("Pw-Secret");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public void A_wrong_outbox_setting_is_refused_when_the_options_are_read()
    {
        var services = new ServiceCollection();
        services.AddCatalogPersistence();
        services.Configure<Catalog.Infrastructure.Outbox.OutboxOptions>(options => options.BatchSize = 0);
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<Catalog.Infrastructure.Outbox.OutboxOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("BatchSize");
    }
}
