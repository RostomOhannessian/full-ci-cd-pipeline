using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-002 and REQ-DOM-004: the interceptor writes one outbox row for each recorded domain event inside the transaction of the save, so the rows and the aggregate changes commit or roll back together (plan section 8.4,
/// ADR-0012). A rollback leaves no row, a second save writes no event twice, and a failed save keeps the events on the aggregate.
/// </summary>
public sealed class OutboxInterceptorTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private string Connection => database.ConnectionString;

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_commit_writes_the_aggregate_and_one_pending_outbox_row_for_each_event_together()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next(), "Keyboards");
        var product = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(), null, Make.Usd(10m), category, clock).Value;
        await using var scope = new CatalogScope(database, clock);

        var result = await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(category);
                scope.Products.Add(product);

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var rows = await SqlProbe.RowsAsync(Connection, "SELECT EventType, SchemaVersion, [Status], Attempts, OccurredAt, CreatedAt, NextAttemptAt FROM catalog.OutboxMessages ORDER BY EventType");
        rows.Select(row => row[0]).Should().Equal("CategoryCreated", "ProductCreated");
        rows.Should().OnlyContain(row => row[1] == "1" && row[2] == "Pending" && row[3] == "0");
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Products")).Should().Be(1);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Categories")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task The_payload_of_a_row_is_the_json_of_its_event()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next(), "Keyboards");
        await using var scope = new CatalogScope(database, clock);

        await scope.SaveAsync(category);

        var payload = await SqlProbe.ScalarAsync<string>(Connection, "SELECT Payload FROM catalog.OutboxMessages WHERE EventType = 'CategoryCreated'");
        payload.Should().Be($$"""{"eventType":"CategoryCreated","schemaVersion":1,"occurredAt":"{{clock.GetUtcNow():O}}","categoryId":"{{category.Id.Value}}","name":"Keyboards"}""");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task The_events_are_cleared_from_the_aggregate_after_the_save_so_a_second_save_writes_none_again()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database, clock);

        await scope.UnitOfWork.ExecuteAsync(
            async token =>
            {
                scope.Categories.Add(category);
                await scope.UnitOfWork.SaveChangesAsync(token);
                category.DomainEvents.Should().BeEmpty();
                await scope.UnitOfWork.SaveChangesAsync(token);

                return Result.Success(1);
            },
            CancellationToken.None);

        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_rollback_leaves_no_outbox_row_and_no_aggregate()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database, clock);

        var result = await scope.UnitOfWork.ExecuteAsync<int>(
            async token =>
            {
                scope.Categories.Add(category);
                await scope.UnitOfWork.SaveChangesAsync(token);

                return Result.Failure<int>(new NotFound("category"));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages")).Should().Be(0);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Categories")).Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_save_that_fails_at_the_commit_leaves_no_outbox_row_of_the_failed_product()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var sku = Counters.Sku();
        var owner = Make.Product(category, clock, Counters.Next(), sku);
        await using (var seed = new CatalogScope(database, clock))
        {
            await seed.SaveAsync(category);
            await seed.SaveAsync(owner);
        }

        var before = await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages");
        var late = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(sku), Make.ProductName(), null, Make.Usd(5m), category, clock).Value;
        await using var scope = new CatalogScope(database, clock);

        var result = await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Products.Add(late);

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);

        result.Error.Should().Be(new Conflict(ConflictReason.SkuAlreadyExists));
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages")).Should().Be(before);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_failed_save_keeps_the_events_on_the_aggregate_and_leaves_no_tracked_outbox_row()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var missing = Make.Category(Counters.Next());
        var product = Make.Product(missing, clock, Counters.Next(), Counters.Sku());
        product.Discontinue(clock);
        await using var scope = new CatalogScope(database, clock);
        await scope.SaveAsync(category);
        scope.Products.Add(product);

        var save = () => scope.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.Should().ThrowAsync<DbUpdateException>("the category of the product does not exist");
        product.DomainEvents.Should().ContainSingle(domainEvent => domainEvent.EventType == "ProductDiscontinued");
        scope.Context.ChangeTracker.Entries<Infrastructure.Persistence.OutboxMessage>().Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_synchronous_save_writes_the_rows_and_clears_the_events_like_the_asynchronous_one()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next(), "Keyboards");
        await using var scope = new CatalogScope(database, clock);
        scope.Categories.Add(category);

        scope.Context.SaveChanges();

        (await SqlProbe.RowsAsync(Connection, "SELECT EventType FROM catalog.OutboxMessages")).Select(row => row[0]).Should().Equal("CategoryCreated");
        category.DomainEvents.Should().BeEmpty();
        scope.Context.ChangeTracker.Entries<Infrastructure.Persistence.OutboxMessage>().Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_failed_synchronous_save_keeps_the_events_on_the_aggregate_and_leaves_no_tracked_outbox_row()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var missing = Make.Category(Counters.Next());
        var product = Make.Product(missing, clock, Counters.Next(), Counters.Sku());
        product.Discontinue(clock);
        await using var scope = new CatalogScope(database, clock);
        scope.Products.Add(product);

        var save = () => scope.Context.SaveChanges();

        save.Should().Throw<DbUpdateException>("the category of the product does not exist");
        product.DomainEvents.Should().ContainSingle(domainEvent => domainEvent.EventType == "ProductDiscontinued");
        scope.Context.ChangeTracker.Entries<Infrastructure.Persistence.OutboxMessage>().Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public void The_interceptor_refuses_a_missing_clock_and_missing_event_data()
    {
        var clock = () => new OutboxInterceptor(null!);
        var interceptor = new OutboxInterceptor(Make.Clock());

        clock.Should().Throw<ArgumentNullException>();
        ((Action)(() => interceptor.SavingChanges(null!, default))).Should().Throw<ArgumentNullException>();
        ((Action)(() => interceptor.SavedChanges(null!, 0))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public async Task An_update_writes_one_row_for_each_part_that_changed_in_the_order_of_the_model()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var first = Make.Category(Counters.Next(), "Keyboards");
        var second = Make.Category(Counters.Next(), "Mice");
        var product = Make.Product(first, clock, Counters.Next(), Counters.Sku(), 89.90m);
        await using (var seed = new CatalogScope(database, clock))
        {
            await seed.SaveAsync(first);
            await seed.SaveAsync(second);
            await seed.SaveAsync(product);
        }

        await database.ResetOutboxAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await using var scope = new CatalogScope(database, clock);
        var loaded = (await scope.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;
        var moved = (await scope.Categories.FindAsync(second.Id, CancellationToken.None))!.Entity;
        loaded.Update(Make.ProductName("Wireless mouse"), null, Make.Usd(24.50m), moved, clock);

        await scope.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(1)), CancellationToken.None);

        var rows = await SqlProbe.RowsAsync(Connection, "SELECT EventType FROM catalog.OutboxMessages ORDER BY Id");
        rows.Select(row => row[0]).Should().BeEquivalentTo(["ProductDetailsChanged", "ProductPriceChanged", "ProductCategoryChanged"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public async Task Every_row_carries_the_version_and_the_time_of_its_event()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database, clock);
        await scope.SaveAsync(category);

        var row = (await SqlProbe.RowsAsync(Connection, "SELECT SchemaVersion, OccurredAt, CreatedAt FROM catalog.OutboxMessages")).Single();

        row[0].Should().Be("1");
        DateTimeOffset.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture).Should().Be(category.CreatedAt);
        DateTimeOffset.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture).Should().Be(clock.GetUtcNow());
    }
}
