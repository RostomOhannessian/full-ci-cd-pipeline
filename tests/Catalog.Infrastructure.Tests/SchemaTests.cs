using AwesomeAssertions;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>REQ-DATA-001: the catalog schema holds the tables, the concurrency tokens, the filtered unique index of the SKU, the keyset indexes, and the decimal precision that plan section 8.4 names.</summary>
public sealed class SchemaTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private string Connection => database.ConnectionString;

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_schema_catalog_holds_the_five_tables_and_the_migrations_history()
    {
        var tables = await SqlProbe.RowsAsync(Connection, "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('catalog') ORDER BY name");

        tables.Select(row => row[0]).Should().Equal("__EFMigrationsHistory", "Categories", "IdempotencyRecords", "OutboxMessages", "OutboxReceipts", "Products");
    }

    [Theory]
    [Trait("Requirement", "REQ-DATA-001")]
    [InlineData("Products")]
    [InlineData("Categories")]
    public async Task An_aggregate_table_has_a_rowversion_column_that_is_never_null(string table)
    {
        var rows = await SqlProbe.RowsAsync(
            Connection,
            "SELECT DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'catalog' AND TABLE_NAME = @table AND COLUMN_NAME = 'RowVersion'",
            ("@table", table));

        rows.Should().ContainSingle().Which.Should().Equal("timestamp", "NO");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_sku_has_a_filtered_unique_index()
    {
        var rows = await SqlProbe.RowsAsync(
            Connection,
            "SELECT is_unique, has_filter, filter_definition FROM sys.indexes WHERE object_id = OBJECT_ID('catalog.Products') AND name = 'UX_Products_Sku'");

        var row = rows.Should().ContainSingle().Subject;
        row[0].Should().Be("True");
        row[1].Should().Be("True");
        row[2].Should().Be("([Sku] IS NOT NULL)");
    }

    [Theory]
    [Trait("Requirement", "REQ-DATA-001")]
    [InlineData("Products", "IX_Products_CreatedAt_Id", "CreatedAt:desc,Id:asc")]
    [InlineData("Products", "IX_Products_Name_Id", "Name:asc,Id:asc")]
    [InlineData("Categories", "IX_Categories_Name_Id", "Name:asc,Id:asc")]
    public async Task The_keyset_indexes_have_the_columns_and_the_directions_of_the_sort_orders(string table, string index, string expected)
    {
        var rows = await SqlProbe.RowsAsync(
            Connection,
            """
            SELECT c.name, ic.is_descending_key
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@table) AND i.name = @index
            ORDER BY ic.key_ordinal
            """,
            ("@table", $"catalog.{table}"),
            ("@index", index));

        string.Join(',', rows.Select(row => $"{row[0]}:{(row[1] == "True" ? "desc" : "asc")}")).Should().Be(expected);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_price_is_a_decimal_with_the_precision_of_the_contract_and_the_scale_of_the_widest_currency()
    {
        var rows = await SqlProbe.RowsAsync(
            Connection,
            "SELECT NUMERIC_PRECISION, NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'catalog' AND TABLE_NAME = 'Products' AND COLUMN_NAME = 'PriceAmount'");

        rows.Should().ContainSingle().Which.Should().Equal("14", "2");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_product_keeps_its_category_with_a_foreign_key_that_never_cascades()
    {
        var rows = await SqlProbe.RowsAsync(
            Connection,
            "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('catalog.Products') AND referenced_object_id = OBJECT_ID('catalog.Categories')");

        rows.Should().ContainSingle().Which.Should().Equal("NO_ACTION");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_model_has_no_change_that_a_migration_does_not_hold()
    {
        await using var context = database.NewContext();

        context.Database.HasPendingModelChanges().Should().BeFalse("the snapshot of the migrations must describe the model, so a change to a mapping needs a new migration");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_read_model_maps_the_columns_of_the_write_model_and_nothing_else()
    {
        await using var write = database.NewContext();
        await using var read = database.NewReadContext();

        foreach (var table in new[] { "Products", "Categories" })
        {
            ColumnsOf(read, table).Should().Equal(ColumnsOf(write, table), $"the read model of {table} must not drift from the write model");
        }
    }

    // The columns of a table with their store type and nullability, from the relational model that the context builds, so the check needs no database.
    private static string[] ColumnsOf(DbContext context, string table)
    {
        var relational = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var mapped = relational.Tables.Single(candidate => candidate.Name == table && candidate.Schema == CatalogDbContext.Schema);

        return [.. mapped.Columns.Select(column => $"{column.Name} {column.StoreType} {(column.IsNullable ? "null" : "not null")}").Order(StringComparer.Ordinal)];
    }
}
