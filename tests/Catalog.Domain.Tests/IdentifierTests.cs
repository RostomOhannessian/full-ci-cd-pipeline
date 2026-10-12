using AwesomeAssertions;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using CsCheck;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-001: product and category identifiers are UUID version 7 values of the RFC 9562 variant, so they sort by creation time, and no other GUID
/// becomes one. The tests build identifiers from a counter or from <see cref="Guid.CreateVersion7(DateTimeOffset)"/> at a fixed time, and never from <see cref="Guid.NewGuid()"/> (REQ-QUA-004).
/// The one test that calls <see cref="Guid.CreateVersion7()"/> checks the real default of the application, and its result does not depend on the value that it makes.
/// </summary>
public sealed class IdentifierTests
{
    /// <summary>Builds a GUID from 16 bytes with the version and the two variant bits that a test asks for. The byte order is the one of RFC 9562.</summary>
    private static Guid WithLayout(byte[] bytes, int version, int variant)
    {
        var copy = (byte[])bytes.Clone();
        copy[6] = (byte)((copy[6] & 0x0F) | (version << 4));
        copy[8] = (byte)((copy[8] & 0x3F) | (variant << 6));

        return new Guid(copy, bigEndian: true);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void The_default_generator_of_the_application_produces_valid_identifiers()
    {
        var guid = Guid.CreateVersion7();

        ProductId.Create(guid).IsSuccess.Should().BeTrue();
        CategoryId.Create(guid).IsSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void An_identifier_made_from_a_fixed_time_is_valid_and_keeps_its_value()
    {
        var guid = Guid.CreateVersion7(Make.Epoch);

        ProductId.Create(guid).Value.Value.Should().Be(guid);
        CategoryId.Create(guid).Value.Value.Should().Be(guid);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    [Trait("Requirement", "REQ-QUA-004")]
    public void The_test_identifiers_are_valid_and_repeatable()
    {
        TestIds.Product(7).Should().Be(TestIds.Product(7));
        TestIds.Product(7).Should().NotBe(TestIds.Product(8));
        TestIds.Category(7).Value.Version.Should().Be(7);
        TestIds.Category(7).Value.Variant.Should().BeInRange(8, 11, "that is how .NET reports the RFC 9562 variant");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void A_version_4_guid_is_refused_with_the_field_of_the_contract()
    {
        // A fixed version 4 GUID, so the test is repeatable: the third group starts with 4 and the fourth with 9.
        var version4 = new Guid("0b7a0a7a-8f6c-4c11-9d5e-2f6a1c3b4d5e");

        var product = ProductId.Create(version4);
        var category = CategoryId.Create(version4);

        version4.Version.Should().Be(4);
        product.Error.Should().Be(DomainError.Invalid("productId", "The product identifier must be a UUID version 7."));
        category.Error.Should().Be(DomainError.Invalid("categoryId", "The category identifier must be a UUID version 7."));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void The_empty_guid_is_refused()
    {
        ProductId.Create(Guid.Empty).IsSuccess.Should().BeFalse();
        CategoryId.Create(Guid.Empty).IsSuccess.Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void The_version_7_check_does_not_accept_the_neighboring_versions()
    {
        var bytes = new byte[16];

        ProductId.Create(WithLayout(bytes, version: 6, variant: 2)).IsSuccess.Should().BeFalse();
        ProductId.Create(WithLayout(bytes, version: 7, variant: 2)).IsSuccess.Should().BeTrue();
        ProductId.Create(WithLayout(bytes, version: 8, variant: 2)).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [Trait("Requirement", "REQ-DOM-001")]
    public void A_version_7_guid_of_another_variant_is_refused(int variant)
    {
        var guid = WithLayout(new byte[16], version: 7, variant);

        ProductId.Create(guid).IsSuccess.Should().BeFalse();
        CategoryId.Create(guid).IsSuccess.Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void Any_bytes_with_the_version_7_layout_make_a_valid_identifier()
    {
        Property.Check(
            Gen.Byte.Array[16],
            bytes =>
            {
                var guid = WithLayout(bytes, version: 7, variant: 2);

                ProductId.Create(guid).Value.Value.Should().Be(guid);
                CategoryId.Create(guid).Value.Value.Should().Be(guid);
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void Any_bytes_with_another_version_or_variant_are_refused()
    {
        var layouts = Gen.Int[0, 15].Select(Gen.Int[0, 3]).Where(layout => layout is not (7, 2));

        Property.Check(
            Gen.Byte.Array[16].Select(layouts),
            sample =>
            {
                var (bytes, (version, variant)) = sample;
                var guid = WithLayout(bytes, version, variant);

                ProductId.Create(guid).IsSuccess.Should().BeFalse();
                CategoryId.Create(guid).IsSuccess.Should().BeFalse();
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void An_identifier_prints_as_a_lower_case_uuid_and_equals_another_of_the_same_value()
    {
        var guid = TestIds.V7(0x0123456789AB);

        ProductId.Create(guid).Value.ToString().Should().Be(guid.ToString("D"));
        ProductId.Create(guid).Value.Should().Be(ProductId.Create(guid).Value);
        CategoryId.Create(guid).Value.ToString().Should().Be(guid.ToString("D"));
        CategoryId.Create(guid).Value.Should().Be(CategoryId.Create(guid).Value);
        ProductId.Create(guid).Value.GetHashCode().Should().Be(ProductId.Create(guid).Value.GetHashCode());
    }
}
