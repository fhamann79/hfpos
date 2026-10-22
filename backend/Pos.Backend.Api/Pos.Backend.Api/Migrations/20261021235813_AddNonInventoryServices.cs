using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNonInventoryServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TracksInventorySnapshot",
                table: "SaleItems",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TracksInventory",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TracksInventorySnapshot",
                table: "CreditNoteItems",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    LOCK TABLE "Products", "SaleItems", "CreditNoteItems" IN ACCESS EXCLUSIVE MODE;
                    IF EXISTS (SELECT 1 FROM "Products" WHERE NOT "TracksInventory")
                       OR EXISTS (SELECT 1 FROM "SaleItems" WHERE NOT "TracksInventorySnapshot")
                       OR EXISTS (SELECT 1 FROM "CreditNoteItems" WHERE NOT "TracksInventorySnapshot") THEN
                        RAISE EXCEPTION 'DEV531_ROLLBACK_UNSAFE: non-inventory behavior must not be reinterpreted as physical';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "TracksInventorySnapshot",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "TracksInventory",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TracksInventorySnapshot",
                table: "CreditNoteItems");
        }
    }
}
