using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pos.Backend.Api.Infrastructure.Data;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    [DbContext(typeof(PosDbContext))]
    [Migration("20260414090000_BE418InventoryMovementIntegrity")]
    public partial class BE418InventoryMovementIntegrity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                table: "InventoryMovements",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "InventoryMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceLineId",
                table: "InventoryMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "InventoryMovements"
                SET "SourceType" = CASE
                        WHEN "Reference" ~ '^SALE-[0-9]+$' THEN 4
                        WHEN "Reference" ~ '^VOID-SALE-[0-9]+$' THEN 5
                        WHEN "Type" = 1 THEN 1
                        WHEN "Type" = 2 THEN 2
                        WHEN "Type" = 3 THEN 3
                        WHEN "Type" = 4 THEN 4
                        WHEN "Type" = 5 THEN 5
                        ELSE 3
                    END,
                    "SourceId" = CASE
                        WHEN "Reference" ~ '^SALE-[0-9]+$' THEN substring("Reference" from 6)::integer
                        WHEN "Reference" ~ '^VOID-SALE-[0-9]+$' THEN substring("Reference" from 11)::integer
                        ELSE NULL
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_CreatedAt",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "EstablishmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_ProductId_CreatedAt",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "EstablishmentId", "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: @"""SourceId"" IS NOT NULL AND ""SourceLineId"" IS NOT NULL AND ""SourceType"" IN (4, 5)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_CreatedAt",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_ProductId_CreatedAt",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "SourceLineId",
                table: "InventoryMovements");
        }
    }
}
