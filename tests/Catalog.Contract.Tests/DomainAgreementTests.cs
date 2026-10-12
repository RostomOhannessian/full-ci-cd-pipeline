using System.Globalization;
using System.Text.RegularExpressions;
using Catalog.Contract.Tests.Support;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using Microsoft.OpenApi;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// REQ-CON-001 and REQ-DOM-002: the domain model and the authored contract state the same bounds, the same sets of values, and the same field names, so a value that the
/// contract accepts is a value that the model accepts, and an error of the model names a field that the client sent. The two are written by hand in different places,
/// so a test is the only thing that keeps them equal (ADR-0029).
/// </summary>
public sealed class DomainAgreementTests
{
    private static readonly IDictionary<string, IOpenApiSchema> Schemas = Authored.Document.Components!.Schemas!;

    private static readonly DomainResult<Product> ProductInDiscontinuedCategory = ProductInADiscontinuedCategory();

    private static DomainResult<Product> ProductInADiscontinuedCategory()
    {
        var clock = TimeProvider.System;
        var category = Category.Create(CategoryId.Create(Guid.CreateVersion7(DateTimeOffset.UnixEpoch)).Value, CategoryName.Create("Old").Value, clock);
        category.Discontinue(clock);

        return Product.Create(
            ProductId.Create(Guid.CreateVersion7(DateTimeOffset.UnixEpoch)).Value,
            Sku.Create("OLD-1").Value,
            ProductName.Create("Old").Value,
            null,
            Money.Zero(Currency.Usd),
            category,
            clock);
    }

    private static bool Matches(string pattern, string value) =>
        Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static IEnumerable<string> EveryStringOver(string alphabet, int maxLength)
    {
        var current = new List<string> { string.Empty };
        foreach (var text in current)
        {
            yield return text;
        }

        for (var length = 1; length <= maxLength; length++)
        {
            current = [.. current.SelectMany(prefix => alphabet.Select(character => prefix + character))];
            foreach (var text in current)
            {
                yield return text;
            }
        }
    }

    /// <summary>Follows a dotted field path through the properties of a schema, such as <c>price.amount</c>.</summary>
    private static bool Resolves(string schemaName, string path)
    {
        IOpenApiSchema schema = Schemas[schemaName];
        foreach (var segment in path.Split('.'))
        {
            if (schema.Properties is null || !schema.Properties.TryGetValue(segment, out var next))
            {
                return false;
            }

            schema = next;
        }

        return true;
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    [Trait("Requirement", "REQ-CON-001")]
    public void The_text_bounds_of_the_model_are_the_bounds_of_the_contract()
    {
        Assert.Equal((Sku.MinLength, Sku.MaxLength), (Schemas["Sku"].MinLength, Schemas["Sku"].MaxLength));
        Assert.Equal((Sku.MinLength, Sku.MaxLength), (Schemas["SkuInput"].MinLength, Schemas["SkuInput"].MaxLength));
        Assert.Equal((1, ProductName.MaxLength), (Schemas["ProductName"].MinLength, Schemas["ProductName"].MaxLength));
        Assert.Equal((1, CategoryName.MaxLength), (Schemas["CategoryName"].MinLength, Schemas["CategoryName"].MaxLength));
        Assert.Null(Schemas["Description"].MinLength);
        Assert.Equal(Description.MaxLength, Schemas["Description"].MaxLength);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    [Trait("Requirement", "REQ-CON-001")]
    public void The_amount_of_the_contract_allows_every_amount_that_the_model_can_hold()
    {
        var pattern = Schemas["Money"].Properties!["amount"].Pattern!;
        var integerDigits = int.Parse(Regex.Match(pattern, @"\[0-9\]\{0,(\d+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups[1].Value, CultureInfo.InvariantCulture) + 1;
        var fractionDigits = int.Parse(Regex.Match(pattern, @"\\\.\[0-9\]\{1,(\d+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups[1].Value, CultureInfo.InvariantCulture);

        Assert.Equal(Money.MaxIntegerDigits, integerDigits);
        Assert.True(fractionDigits >= Currency.All.Max(currency => currency.MinorUnits), "The pattern must allow the decimal places of every currency; the currency narrows it.");

        foreach (var currency in Currency.All)
        {
            var smallest = Money.Create(1m / (decimal)Math.Pow(10, currency.MinorUnits), currency).Value;
            var largest = Money.Create(Money.Limit - (1m / (decimal)Math.Pow(10, currency.MinorUnits)), currency).Value;

            foreach (var money in new[] { Money.Zero(currency), smallest, largest })
            {
                Assert.True(Matches(pattern, money.Amount.ToString(CultureInfo.InvariantCulture)), $"{money} must match {pattern}");
            }
        }

        Assert.False(Matches(pattern, Money.Limit.ToString(CultureInfo.InvariantCulture)), "The limit has 13 integer digits, so the contract refuses it too.");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    [Trait("Requirement", "REQ-CON-001")]
    public void The_currencies_and_the_lifecycle_statuses_are_the_same_values_in_the_same_order()
    {
        Assert.Equal(Currency.All.Select(currency => currency.Code), Schemas["CurrencyCode"].Enum!.Select(value => value!.GetValue<string>()));
        Assert.Equal(
            Enum.GetNames<LifecycleStatus>().Select(name => name.ToLowerInvariant()),
            Schemas["LifecycleStatus"].Enum!.Select(value => value!.GetValue<string>()));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    [Trait("Requirement", "REQ-CON-001")]
    public void A_sku_is_accepted_by_the_model_exactly_when_the_contract_accepts_it()
    {
        var input = Schemas["SkuInput"];
        var canonical = Schemas["Sku"];
        var checkedCount = 0;

        foreach (var text in EveryStringOver("aZ7-_", 6).Concat(Enumerable.Range(0, 40).Select(length => new string('a', length))))
        {
            var contractAccepts = text.Length >= input.MinLength && text.Length <= input.MaxLength && Matches(input.Pattern!, text);
            var result = Sku.Create(text);

            Assert.True(contractAccepts == result.IsSuccess, $"'{text}': the contract says {contractAccepts} and the model says {result.IsSuccess}.");
            if (result.IsSuccess)
            {
                Assert.True(Matches(canonical.Pattern!, result.Value.Value), $"The model made '{result.Value.Value}' from '{text}', and the contract does not accept it as a canonical SKU.");
                Assert.InRange(result.Value.Value.Length, canonical.MinLength!.Value, canonical.MaxLength!.Value);
            }

            checkedCount++;
        }

        Assert.True(checkedCount > 19_000, "The corpus is the complete set of short strings over five characters, and it should not shrink unnoticed.");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    [Trait("Requirement", "REQ-CON-001")]
    public void An_error_of_the_model_names_a_field_that_the_client_sent()
    {
        var tooLong = new string('x', 5000);
        var productFields = new[]
        {
            Sku.Create(string.Empty).Error!.Field,
            ProductName.Create(string.Empty).Error!.Field,
            Description.Create(tooLong).Error!.Field,
            Money.Create(-1m, Currency.Usd).Within("price").Error!.Field,
            Currency.Create("XXX").Within("price").Error!.Field,
            ProductInDiscontinuedCategory.Error!.Field,
        };
        var categoryFields = new[] { CategoryName.Create(string.Empty).Error!.Field };

        Assert.Equal(["sku", "name", "description", "price.amount", "price.currency", "categoryId"], productFields);
        Assert.All(productFields, field => Assert.True(Resolves("ProductCreate", field), $"ProductCreate has no field {field}."));
        Assert.All(productFields.Where(field => field != "sku"), field => Assert.True(Resolves("ProductUpdate", field), $"ProductUpdate has no field {field}."));
        Assert.All(categoryFields, field => Assert.True(Resolves("CategoryCreate", field) && Resolves("CategoryUpdate", field), $"The category bodies have no field {field}."));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    [Trait("Requirement", "REQ-CON-001")]
    public void An_error_of_an_identifier_names_a_path_parameter_of_the_contract()
    {
        var parameters = Authored.Document.Components!.Parameters!.Values.Select(parameter => parameter.Name).ToList();

        Assert.Contains(ProductId.Create(Guid.Empty).Error!.Field, parameters);
        Assert.Contains(CategoryId.Create(Guid.Empty).Error!.Field, parameters);
        Assert.Equal("uuid", Schemas["Id"].Format);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    [Trait("Requirement", "REQ-DOM-003")]
    public void The_domain_model_page_lists_every_event_with_its_properties_and_every_error_code()
    {
        var page = Authored.ReadText("docs", "reference", "domain-model.md");

        string SectionRows(string heading)
        {
            var start = page.IndexOf($"\n## {heading}\n", StringComparison.Ordinal);
            Assert.True(start >= 0, $"The page has no section '{heading}'.");
            var end = page.IndexOf("\n## ", start + 1, StringComparison.Ordinal);

            return page[start..(end < 0 ? page.Length : end)];
        }

        var eventRows = Regex.Matches(SectionRows("Events"), @"^\| `(?<name>\w+)` \| .+? \| (?<properties>`.+`) \|$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .ToDictionary(row => row.Groups["name"].Value, row => Regex.Matches(row.Groups["properties"].Value, @"`(\w+)`", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups[1].Value).ToList());
        var events = typeof(Category).Assembly.GetTypes()
            .Where(type => type.IsClass && typeof(IDomainEvent).IsAssignableFrom(type))
            .ToDictionary(type => type.Name, type => type.GetProperties().Select(property => property.Name).Where(name => name is not ("EventType" or "SchemaVersion" or "EqualityContract")).ToList());

        Assert.Equal(events.Keys.Order(StringComparer.Ordinal), eventRows.Keys.Order(StringComparer.Ordinal));
        Assert.All(events, entry => Assert.Equal(entry.Value, eventRows[entry.Key]));

        var errorCodes = Regex.Matches(SectionRows("Errors"), @"^\| `(?<name>\w+)` \| `", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(row => row.Groups["name"].Value);
        Assert.Equal(Enum.GetNames<DomainErrorCode>().Order(StringComparer.Ordinal), errorCodes.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    [Trait("Requirement", "REQ-CON-001")]
    public void Every_kind_of_domain_failure_maps_to_an_entry_of_the_error_catalog()
    {
        var catalog = Authored.ReadText("docs", "reference", "error-catalog.md");
        var entryOf = new Dictionary<DomainErrorCode, string>
        {
            [DomainErrorCode.Invalid] = "validation-failed",
            [DomainErrorCode.CategoryNotActive] = "validation-failed",
            [DomainErrorCode.ProductDiscontinued] = "product-discontinued",
        };

        Assert.Equal(Enum.GetValues<DomainErrorCode>().Order(), entryOf.Keys.Order());
        Assert.All(entryOf.Values, entry => Assert.Contains($"\n## {entry}\n", catalog, StringComparison.Ordinal));
    }
}
