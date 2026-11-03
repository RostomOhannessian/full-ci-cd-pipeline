using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-002 and REQ-DATA-001: the unit of work wraps one context transaction. A success commits, a refusal or a fault rolls back, a stale writer and a taken SKU come back as values, and a hook runs inside
/// the transaction, so a record that it writes exists exactly when the effect does (plan section 8.4, ADR-0032).
/// </summary>
public sealed class UnitOfWorkTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private string Connection => database.ConnectionString;

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_success_commits_the_changes_that_the_work_made()
    {
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database);

        var result = await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(category);

                return Task.FromResult(Result.Success("created"));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("created");
        (await CountCategoriesAsync(category.Id.Value)).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_refusal_rolls_back_even_the_changes_that_the_work_already_saved()
    {
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database);

        var result = await scope.UnitOfWork.ExecuteAsync<string>(
            async token =>
            {
                scope.Categories.Add(category);
                await scope.UnitOfWork.SaveChangesAsync(token);

                return Result.Failure<string>(new NotFound("category"));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(new NotFound("category"));
        (await CountCategoriesAsync(category.Id.Value)).Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_fault_rolls_back_and_the_exception_reaches_the_caller()
    {
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database);

        var act = () => scope.UnitOfWork.ExecuteAsync<string>(
            async token =>
            {
                scope.Categories.Add(category);
                await scope.UnitOfWork.SaveChangesAsync(token);

                throw new InvalidOperationException("The work failed.");
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The work failed.");
        (await CountCategoriesAsync(category.Id.Value)).Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_writer_that_loaded_an_old_version_gets_precondition_failed_and_changes_nothing()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku());
        await using (var seed = new CatalogScope(database))
        {
            await seed.SaveAsync(category);
            await seed.SaveAsync(product);
        }

        await using var first = new CatalogScope(database);
        await using var second = new CatalogScope(database);
        var loadedByFirst = (await first.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;
        var loadedBySecond = (await second.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;
        var categoryOfFirst = (await first.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity;
        var categoryOfSecond = (await second.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity;

        loadedByFirst.Update(Make.ProductName("Changed by the first"), null, loadedByFirst.Price, categoryOfFirst, clock);
        loadedBySecond.Update(Make.ProductName("Changed by the second"), null, loadedBySecond.Price, categoryOfSecond, clock);

        var firstResult = await first.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(1)), CancellationToken.None);
        var secondResult = await second.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(2)), CancellationToken.None);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.Error.Should().Be(new PreconditionFailed());
        await using var reader = new CatalogScope(database);
        (await reader.Products.FindAsync(product.Id, CancellationToken.None))!.Entity.Name.Value.Should().Be("Changed by the first");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_scope_that_was_refused_can_read_the_current_version_again()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku());
        await using (var seed = new CatalogScope(database))
        {
            await seed.SaveAsync(category);
            await seed.SaveAsync(product);
        }

        await using var stale = new CatalogScope(database);
        await using var other = new CatalogScope(database);
        var staleLoad = await stale.Products.FindAsync(product.Id, CancellationToken.None);
        var otherLoad = await other.Products.FindAsync(product.Id, CancellationToken.None);
        var otherCategory = (await other.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity;
        var staleCategory = (await stale.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity;

        otherLoad!.Entity.Discontinue(clock);
        (await other.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(0)), CancellationToken.None)).IsSuccess.Should().BeTrue();
        staleLoad!.Entity.Update(Make.ProductName("Too late"), null, staleLoad.Entity.Price, staleCategory, clock);
        (await stale.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(0)), CancellationToken.None)).Error.Should().Be(new PreconditionFailed());

        var reloaded = await stale.Products.FindAsync(product.Id, CancellationToken.None);

        reloaded!.Entity.Status.Should().Be(LifecycleStatus.Discontinued);
        reloaded.Tag.Should().NotBe(staleLoad.Tag);
        otherCategory.Should().NotBeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_sku_that_another_product_owns_comes_back_as_a_conflict_and_nothing_is_stored()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var sku = Counters.Sku();
        var owner = Make.Product(category, clock, Counters.Next(), sku);
        var late = Make.Product(category, clock, Counters.Next(), sku);
        await using (var seed = new CatalogScope(database))
        {
            await seed.SaveAsync(category);
            await seed.SaveAsync(owner);
        }

        await using var scope = new CatalogScope(database);
        var result = await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Products.Add(late);

                return Task.FromResult(Result.Success(late.Id.Value));
            },
            CancellationToken.None);

        result.Error.Should().Be(new Conflict(ConflictReason.SkuAlreadyExists));
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Products WHERE Sku = @sku", ("@sku", sku))).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_taken_sku_is_a_conflict_even_when_the_work_saved_before_the_commit()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var sku = Counters.Sku();
        var owner = Make.Product(category, clock, Counters.Next(), sku);
        var late = Make.Product(category, clock, Counters.Next(), sku);
        await using (var seed = new CatalogScope(database))
        {
            await seed.SaveAsync(category);
            await seed.SaveAsync(owner);
        }

        await using var scope = new CatalogScope(database);
        var result = await scope.UnitOfWork.ExecuteAsync(
            async token =>
            {
                scope.Products.Add(late);
                await scope.UnitOfWork.SaveChangesAsync(token);

                return Result.Success(late.Id.Value);
            },
            CancellationToken.None);

        result.Error.Should().Be(new Conflict(ConflictReason.SkuAlreadyExists));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_duplicate_identifier_is_a_fault_and_is_never_reported_as_a_taken_sku()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var id = Counters.Next();
        var first = Make.Product(category, clock, id, Counters.Sku());
        var again = Make.Product(category, clock, id, Counters.Sku());
        await using (var seed = new CatalogScope(database))
        {
            await seed.SaveAsync(category);
            await seed.SaveAsync(first);
        }

        await using var scope = new CatalogScope(database);
        var act = () => scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Products.Add(again);

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_product_in_a_category_that_does_not_exist_is_a_fault_because_the_foreign_key_holds()
    {
        var clock = Make.Clock();
        var missing = Make.Category(Counters.Next());
        var product = Make.Product(missing, clock, Counters.Next(), Counters.Sku());

        await using var scope = new CatalogScope(database);
        var act = () => scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Products.Add(product);

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_hook_runs_after_the_save_inside_the_transaction_and_receives_the_value_of_the_work()
    {
        var category = Make.Category(Counters.Next());
        object? received = null;
        int? seenInside = null;
        int? seenOutside = null;
        await using var scope = new CatalogScope(database);

        scope.UnitOfWork.BeforeCommit(
            async (value, token) =>
            {
                received = value;
                seenInside = (await scope.Context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM catalog.Categories WHERE Id = {category.Id.Value}").ToListAsync(token)).Single();

                // READPAST skips the row that the open transaction has locked, so a second connection that sees nothing proves that the row is not committed yet.
                seenOutside = await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Categories WITH (READPAST) WHERE Id = @id", ("@id", category.Id.Value));
            });

        var result = await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(category);

                return Task.FromResult(Result.Success("created"));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        received.Should().Be("created");
        seenInside.Should().Be(1);
        seenOutside.Should().Be(0);
        (await CountCategoriesAsync(category.Id.Value)).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_hook_that_throws_rolls_back_the_changes_that_were_saved_before_it()
    {
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database);
        scope.UnitOfWork.BeforeCommit((_, _) => throw new InvalidOperationException("The record could not be written."));

        var act = () => scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(category);

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The record could not be written.");
        (await CountCategoriesAsync(category.Id.Value)).Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_hook_of_a_run_that_failed_does_not_run_in_the_next_run_of_the_scope()
    {
        var ran = 0;
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database);
        scope.UnitOfWork.BeforeCommit((_, _) =>
        {
            ran++;

            return Task.CompletedTask;
        });

        await scope.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Failure<int>(new NotFound("category"))), CancellationToken.None);
        await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(category);

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);

        ran.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_run_inside_a_run_is_refused()
    {
        await using var scope = new CatalogScope(database);

        var act = () => scope.UnitOfWork.ExecuteAsync(
            _ => scope.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(1)), CancellationToken.None),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not nest*");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task An_added_aggregate_has_a_version_after_the_work_saves_and_the_version_is_the_stored_one()
    {
        var category = Make.Category(Counters.Next());
        await using var scope = new CatalogScope(database);
        Application.Persistence.EntityTag? duringWork = null;

        await scope.UnitOfWork.ExecuteAsync(
            async token =>
            {
                scope.Categories.Add(category);
                var act = () => scope.Categories.TagOf(category);
                act.Should().Throw<InvalidOperationException>().WithMessage("*not been saved*");

                await scope.UnitOfWork.SaveChangesAsync(token);
                duringWork = scope.Categories.TagOf(category);

                return Result.Success(1);
            },
            CancellationToken.None);

        await using var reader = new CatalogScope(database);
        (await reader.Categories.FindAsync(category.Id, CancellationToken.None))!.Tag.Should().Be(duringWork);
    }

    private Task<int> CountCategoriesAsync(Guid id) =>
        SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Categories WHERE Id = @id", ("@id", id));
}
