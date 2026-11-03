using Catalog.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Mapping;

/// <summary>Maps the <see cref="Category"/> aggregate to the table <c>catalog.Categories</c> (plan section 8.4). The Domain carries no persistence attributes, so this class holds the whole mapping.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <summary>The name of the shadow property that holds the row version. The aggregate never sees it, and the repository reads it to make the <c>ETag</c>.</summary>
    public const string RowVersion = "RowVersion";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Categories");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).HasConversion(new CategoryIdConverter()).ValueGeneratedNever();

        builder.Property(category => category.Name).HasConversion(new CategoryNameConverter()).HasMaxLength(CategoryName.MaxLength).IsRequired();
        builder.Property(category => category.Status).HasConversion<string>().HasMaxLength(StatusColumn.Length).IsUnicode(false).IsRequired();
        builder.Property(category => category.CreatedAt).IsRequired();
        builder.Property(category => category.UpdatedAt).IsRequired();

        // The row version is a rowversion column, so SQL Server changes it on every write, and EF Core puts it in the WHERE clause of each update. A stale write changes no row and EF Core reports a concurrency failure.
        builder.Property<byte[]>(RowVersion).IsRowVersion().IsRequired();

        // The list of categories is ordered by name and then by identifier, and the keyset query reads this index in order.
        builder.HasIndex(category => new { category.Name, category.Id }).HasDatabaseName("IX_Categories_Name_Id");

        builder.Ignore(category => category.DomainEvents);
        builder.Ignore(category => category.IsActive);
    }
}

/// <summary>The width of the column that holds a lifecycle status as text.</summary>
internal static class StatusColumn
{
    /// <summary>The number of characters of the longest status name, with room for a later one.</summary>
    public const int Length = 16;
}
