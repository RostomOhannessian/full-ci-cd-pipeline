using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Mapping;

/// <summary>Maps the <see cref="Product"/> aggregate to the table <c>catalog.Products</c> (plan section 8.4). The Domain carries no persistence attributes, so this class holds the whole mapping.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <summary>The name of the unique index of the SKU, which the unit of work reads in a violation to tell a taken SKU from another failure.</summary>
    public const string SkuIndexName = "UX_Products_Sku";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Products");

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).HasConversion(new ProductIdConverter()).ValueGeneratedNever();

        // The SKU is ASCII in capital letters, so a varchar column and the default collation compare it as the model does.
        builder.Property(product => product.Sku).HasConversion(new SkuConverter()).HasMaxLength(Sku.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(product => product.Name).HasConversion(new ProductNameConverter()).HasMaxLength(ProductName.MaxLength).IsRequired();
        builder.Property(product => product.Description).HasConversion(new DescriptionConverter()).HasMaxLength(Description.MaxLength).IsRequired(false);
        builder.Property(product => product.CategoryId).HasConversion(new CategoryIdConverter()).IsRequired();
        builder.Property(product => product.Status).HasConversion<string>().HasMaxLength(StatusColumn.Length).IsUnicode(false).IsRequired();
        builder.Property(product => product.CreatedAt).IsRequired();
        builder.Property(product => product.UpdatedAt).IsRequired();

        // A price is two columns. The precision holds the integer digits of the contract and the decimal places of the currency with the most, so no amount of the model is cut or rounded.
        builder.ComplexProperty(
            product => product.Price,
            price =>
            {
                price.Property(money => money.Amount).HasColumnName("PriceAmount").HasPrecision(MoneyColumn.Precision(), MoneyColumn.Scale());
                price.Property(money => money.Currency).HasColumnName("PriceCurrency").HasConversion(new CurrencyConverter()).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            });

        builder.Property<byte[]>(CategoryConfiguration.RowVersion).IsRowVersion().IsRequired();

        // A product keeps its category, and a category that has products is never deleted, because a category is discontinued and not removed.
        builder.HasOne<Category>().WithMany().HasForeignKey(product => product.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // The unique index is the rule that cannot be raced. It holds every product, whatever its status, because a discontinued product still owns its SKU. The filter excludes no row today, because
        // the SKU is required. It is kept because plan section 8.4 and REQ-DATA-001 name a filtered index.
        builder.HasIndex(product => product.Sku).IsUnique().HasDatabaseName(SkuIndexName).HasFilter("[Sku] IS NOT NULL");

        // The keyset indexes of plan section 8.4: newest first and then by identifier, and by name and then by identifier.
        builder.HasIndex(product => new { product.CreatedAt, product.Id }).IsDescending(true, false).HasDatabaseName("IX_Products_CreatedAt_Id");
        builder.HasIndex(product => new { product.Name, product.Id }).HasDatabaseName("IX_Products_Name_Id");

        builder.Ignore(product => product.DomainEvents);
        builder.Ignore(product => product.IsActive);
    }
}
