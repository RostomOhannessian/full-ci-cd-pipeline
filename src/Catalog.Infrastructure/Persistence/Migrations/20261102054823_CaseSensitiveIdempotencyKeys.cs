using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes the caller and the key of an idempotency record case-sensitive with a binary collation, so the keys "Abc" and "abc" are two keys (ADR-0036). The primary key depends on both columns, and SQL Server refuses
    /// to change the collation of a column that an index uses, so the migration drops the primary key, changes the columns, and creates the key again. It is written by hand after the generator, because the generator
    /// emits the two <c>ALTER COLUMN</c> statements without the drop. The table holds only short-lived rows, and no key can collide, because the old collation was the less strict one.
    /// </summary>
    public partial class CaseSensitiveIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "catalog",
                table: "IdempotencyRecords");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                schema: "catalog",
                table: "IdempotencyRecords",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldUnicode: false,
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "CallerId",
                schema: "catalog",
                table: "IdempotencyRecords",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AddPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "catalog",
                table: "IdempotencyRecords",
                columns: new[] { "CallerId", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "catalog",
                table: "IdempotencyRecords");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                schema: "catalog",
                table: "IdempotencyRecords",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldUnicode: false,
                oldMaxLength: 128,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "CallerId",
                schema: "catalog",
                table: "IdempotencyRecords",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "catalog",
                table: "IdempotencyRecords",
                columns: new[] { "CallerId", "Key" });
        }
    }
}
