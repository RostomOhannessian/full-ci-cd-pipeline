using Catalog.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Reading;

/// <summary>A product as the read model stores it: plain values with no rules, which the query side turns into a view.</summary>
public sealed class ProductRow
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the SKU.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the description, or null.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the amount of the price.</summary>
    public decimal PriceAmount { get; set; }

    /// <summary>Gets or sets the ISO 4217 code of the currency of the price.</summary>
    public string PriceCurrency { get; set; } = string.Empty;

    /// <summary>Gets or sets the identifier of the category.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Gets or sets the lifecycle status.</summary>
    public LifecycleStatus Status { get; set; }

    /// <summary>Gets or sets when the product was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the product last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the row version, which becomes the <c>ETag</c>.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A category as the read model stores it.</summary>
public sealed class CategoryRow
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the lifecycle status.</summary>
    public LifecycleStatus Status { get; set; }

    /// <summary>Gets or sets when the category was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the category last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the row version, which becomes the <c>ETag</c>.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// The read model of the catalog: the two tables that the query side reads, as plain rows with primitive identifiers, so the keyset queries compare and sort real columns and never load an aggregate (plan section 8.4).
/// It maps the tables of <see cref="CatalogDbContext"/> and never changes them: it has no migrations, and it does not track what it reads. A test compares its columns with those of the write model, so the two cannot drift.
/// </summary>
/// <param name="options">The options, which choose the provider, the connection, and the interceptors.</param>
public sealed class CatalogReadContext(DbContextOptions<CatalogReadContext> options) : DbContext(options)
{
    /// <summary>Gets the product rows.</summary>
    public DbSet<ProductRow> Products => Set<ProductRow>();

    /// <summary>Gets the category rows.</summary>
    public DbSet<CategoryRow> Categories => Set<CategoryRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(CatalogDbContext.Schema);

        modelBuilder.Entity<ProductRow>(Configure);
        modelBuilder.Entity<CategoryRow>(Configure);
    }

    private static void Configure(EntityTypeBuilder<ProductRow> builder)
    {
        builder.ToTable("Products", table => table.ExcludeFromMigrations());
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Sku).HasMaxLength(Domain.Products.Sku.MaxLength).IsUnicode(false);
        builder.Property(row => row.Name).HasMaxLength(Domain.Products.ProductName.MaxLength);
        builder.Property(row => row.Description).HasMaxLength(Domain.Products.Description.MaxLength);
        builder.Property(row => row.PriceAmount).HasPrecision(Mapping.MoneyColumn.Precision(), Mapping.MoneyColumn.Scale());
        builder.Property(row => row.PriceCurrency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(row => row.Status).HasConversion<string>().HasMaxLength(Mapping.StatusColumn.Length).IsUnicode(false);
        builder.Property(row => row.RowVersion).IsRowVersion();
    }

    private static void Configure(EntityTypeBuilder<CategoryRow> builder)
    {
        builder.ToTable("Categories", table => table.ExcludeFromMigrations());
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Name).HasMaxLength(Domain.Categories.CategoryName.MaxLength);
        builder.Property(row => row.Status).HasConversion<string>().HasMaxLength(Mapping.StatusColumn.Length).IsUnicode(false);
        builder.Property(row => row.RowVersion).IsRowVersion();
    }
}
