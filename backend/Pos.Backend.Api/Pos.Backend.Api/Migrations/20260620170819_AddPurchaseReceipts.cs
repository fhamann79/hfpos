using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.CreateTable(
                name: "PurchaseReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SupplierDocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReceiptDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_Establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PurchaseReceiptId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PreviousProductCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AppliedProductCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptItems_PurchaseReceipts_PurchaseReceiptId",
                        column: x => x.PurchaseReceiptId,
                        principalTable: "PurchaseReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptItems_ProductId",
                table: "PurchaseReceiptItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptItems_PurchaseReceiptId",
                table: "PurchaseReceiptItems",
                column: "PurchaseReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_EstablishmentId_ReceiptDate",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "EstablishmentId", "ReceiptDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_ReceiptNumber",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "ReceiptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_SupplierDocumentNumber",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "SupplierDocumentNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_SupplierId_ReceiptDate",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "SupplierId", "ReceiptDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CreatedByUserId",
                table: "PurchaseReceipts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_EstablishmentId",
                table: "PurchaseReceipts",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_SupplierId",
                table: "PurchaseReceipts",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseReceiptItems");

            migrationBuilder.DropTable(
                name: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5)");
        }
    }
}
