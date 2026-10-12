using AwesomeAssertions;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using CsCheck;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-003, REQ-DOM-004, and REQ-DOM-005 as properties: after any sequence of updates and discontinuations, a refused call has changed nothing, an accepted call
/// has changed exactly what it asked for and recorded exactly one event for each part, a discontinued product has the price and the category that it
/// had when it was discontinued, and a product is never moved into a discontinued category. Each check compares the product with a snapshot that the test took before the call.
/// </summary>
public sealed class ProductLifecyclePropertyTests
{
    private static readonly string[] Names = ["Keyboard", "Mouse", "Monitor"];

    private static readonly decimal[] Prices = [0m, 9.99m, 89.90m, 1234.50m];

    /// <summary>One step: what to do (0 to 3 update, 4 discontinue the product, 5 discontinue a category) and the arguments of an update.</summary>
    private static readonly Gen<(int Kind, int Name, int Description, int Price, int Category)> Step =
        Gen.Select(Gen.Int[0, 5], Gen.Int[0, Names.Length - 1], Gen.Int[0, 2], Gen.Int[0, Prices.Length - 1], Gen.Int[0, 3])
            .Select(parts => (parts.Item1, parts.Item2, parts.Item3, parts.Item4, parts.Item5));

    private sealed record Snapshot(
        ProductName Name,
        Description? Description,
        Money Price,
        CategoryId CategoryId,
        LifecycleStatus Status,
        DateTimeOffset UpdatedAt,
        int EventCount);

    private static Snapshot Take(Product product) =>
        new(product.Name, product.Description, product.Price, product.CategoryId, product.Status, product.UpdatedAt, product.DomainEvents.Count);

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    public void Any_sequence_of_operations_keeps_the_rules_of_the_lifecycle()
    {
        Property.Check(
            Step.List[1, 40],
            steps =>
            {
                var clock = Make.Clock();
                var categories = new[] { Make.Category(1, "One"), Make.Category(2, "Two"), Make.Category(3, "Three"), Make.DiscontinuedCategory(4, "Four") };
                var product = Make.Product(categories[0], clock);
                var createdAt = product.CreatedAt;
                var discontinuedWith = (Snapshot?)null;

                foreach (var (kind, nameIndex, descriptionIndex, priceIndex, categoryIndex) in steps)
                {
                    clock.Advance(TimeSpan.FromMinutes(1));
                    var before = Take(product);

                    if (kind == 4)
                    {
                        product.Discontinue(clock);
                        discontinuedWith ??= Take(product);
                        product.Status.Should().Be(LifecycleStatus.Discontinued);
                        product.DomainEvents.Count.Should().Be(before.Status == LifecycleStatus.Active ? before.EventCount + 1 : before.EventCount);
                    }
                    else if (kind == 5)
                    {
                        categories[categoryIndex].Discontinue(clock);
                        Take(product).Should().Be(before, "discontinuing a category does not touch its products");
                    }
                    else
                    {
                        var name = Make.ProductName(Names[nameIndex]);
                        var description = descriptionIndex == 0 ? null : Make.Description($"Description {descriptionIndex}");
                        var price = Make.Usd(Prices[priceIndex]);
                        var category = categories[categoryIndex];
                        var priceChanges = price != before.Price;
                        var categoryChanges = category.Id != before.CategoryId;
                        var detailsChange = name != before.Name || description != before.Description;

                        var result = product.Update(name, description, price, category, clock);

                        if (before.Status == LifecycleStatus.Discontinued && (priceChanges || categoryChanges))
                        {
                            result.IsSuccess.Should().BeFalse();
                            result.Error!.Code.Should().Be(DomainErrorCode.ProductDiscontinued);
                        }
                        else if (categoryChanges && !category.IsActive)
                        {
                            result.IsSuccess.Should().BeFalse();
                            result.Error!.Code.Should().Be(DomainErrorCode.CategoryNotActive);
                        }
                        else
                        {
                            result.IsSuccess.Should().BeTrue();
                        }

                        if (result.IsSuccess)
                        {
                            var changes = (detailsChange ? 1 : 0) + (priceChanges ? 1 : 0) + (categoryChanges ? 1 : 0);
                            product.Name.Should().Be(name);
                            product.Description.Should().Be(description);
                            product.Price.Should().Be(price);
                            product.CategoryId.Should().Be(category.Id);
                            product.DomainEvents.Count.Should().Be(before.EventCount + changes);
                            product.UpdatedAt.Should().Be(changes == 0 ? before.UpdatedAt : clock.GetUtcNow());
                            product.DomainEvents.Skip(before.EventCount).Should().OnlyContain(domainEvent => domainEvent.OccurredAt == clock.GetUtcNow());
                            if (categoryChanges)
                            {
                                category.IsActive.Should().BeTrue("a product moves only to an active category");
                            }
                        }
                        else
                        {
                            Take(product).Should().Be(before, "a refused update changes nothing");
                        }
                    }

                    if (discontinuedWith is not null)
                    {
                        product.Price.Should().Be(discontinuedWith.Price);
                        product.CategoryId.Should().Be(discontinuedWith.CategoryId);
                        product.Status.Should().Be(LifecycleStatus.Discontinued);
                    }

                    product.CreatedAt.Should().Be(createdAt);
                    product.UpdatedAt.Should().BeOnOrAfter(before.UpdatedAt);
                }
            });
    }
}
