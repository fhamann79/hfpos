using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseCostProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProductCostAfterCancellation",
                table: "PurchaseReceiptItems",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProductCostChangedOnCancellation",
                table: "PurchaseReceiptItems",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentCostEventId",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastCostRevision",
                table: "Products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ProductCostEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    PreviousCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    PurchaseReceiptItemId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCostEvents", x => x.Id);
                    table.CheckConstraint("CK_ProductCostEvents_Source", "(\"SourceType\" = 2 AND \"PurchaseReceiptItemId\" IS NOT NULL) OR (\"SourceType\" IN (0, 1) AND \"PurchaseReceiptItemId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ProductCostEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCostEvents_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCostEvents_PurchaseReceiptItems_PurchaseReceiptItemId",
                        column: x => x.PurchaseReceiptItemId,
                        principalTable: "PurchaseReceiptItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCostEvents_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CurrentCostEventId",
                table: "Products",
                column: "CurrentCostEventId",
                unique: true,
                filter: "\"CurrentCostEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostEvents_CompanyId",
                table: "ProductCostEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostEvents_CreatedByUserId",
                table: "ProductCostEvents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostEvents_ProductId_Revision",
                table: "ProductCostEvents",
                columns: new[] { "ProductId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostEvents_PurchaseReceiptItemId",
                table: "ProductCostEvents",
                column: "PurchaseReceiptItemId",
                unique: true,
                filter: "\"PurchaseReceiptItemId\" IS NOT NULL");

            // Historical costs cannot be attributed reliably, so preserve each as a Legacy event.
            migrationBuilder.Sql(
                """
                INSERT INTO "ProductCostEvents"
                    ("CompanyId", "ProductId", "Revision", "PreviousCost", "Cost",
                     "SourceType", "PurchaseReceiptItemId", "CreatedAt", "CreatedByUserId")
                SELECT
                    p."CompanyId", p."Id", 0, p."Cost", p."Cost",
                    0, NULL, p."CreatedAt", NULL
                FROM "Products" AS p
                ORDER BY p."Id";

                UPDATE "Products" AS p
                SET "CurrentCostEventId" = cost_event."Id"
                FROM "ProductCostEvents" AS cost_event
                WHERE cost_event."ProductId" = p."Id"
                  AND cost_event."Revision" = 0
                  AND cost_event."SourceType" = 0;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductCostEvents_CurrentCostEventId",
                table: "Products",
                column: "CurrentCostEventId",
                principalTable: "ProductCostEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductCostEvents_CurrentCostEventId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductCostEvents");

            migrationBuilder.DropIndex(
                name: "IX_Products_CurrentCostEventId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ProductCostAfterCancellation",
                table: "PurchaseReceiptItems");

            migrationBuilder.DropColumn(
                name: "ProductCostChangedOnCancellation",
                table: "PurchaseReceiptItems");

            migrationBuilder.DropColumn(
                name: "CurrentCostEventId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LastCostRevision",
                table: "Products");
        }
    }
}
