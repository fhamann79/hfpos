using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.CreateTable(
                name: "InventoryTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SourceEstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    DestinationEstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneIdSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransfers", x => x.Id);
                    table.CheckConstraint("CK_InventoryTransfers_DifferentEstablishments", "\"SourceEstablishmentId\" <> \"DestinationEstablishmentId\"");
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_Establishments_DestinationEstablishmentId",
                        column: x => x.DestinationEstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_Establishments_SourceEstablishmentId",
                        column: x => x.SourceEstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransferItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventoryTransferId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SourceMovementId = table.Column<int>(type: "integer", nullable: false),
                    DestinationMovementId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransferItems", x => x.Id);
                    table.CheckConstraint("CK_InventoryTransferItems_Quantity_Positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_InventoryTransferItems_InventoryMovements_DestinationMoveme~",
                        column: x => x.DestinationMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransferItems_InventoryMovements_SourceMovementId",
                        column: x => x.SourceMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransferItems_InventoryTransfers_InventoryTransfer~",
                        column: x => x.InventoryTransferId,
                        principalTable: "InventoryTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryTransferItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5, 6, 7, 8, 9, 10)");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransferItems_DestinationMovementId",
                table: "InventoryTransferItems",
                column: "DestinationMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransferItems_InventoryTransferId_ProductId",
                table: "InventoryTransferItems",
                columns: new[] { "InventoryTransferId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransferItems_ProductId",
                table: "InventoryTransferItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransferItems_SourceMovementId",
                table: "InventoryTransferItems",
                column: "SourceMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_CompanyId_DestinationEstablishmentId_Bus~",
                table: "InventoryTransfers",
                columns: new[] { "CompanyId", "DestinationEstablishmentId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_CompanyId_RequestId",
                table: "InventoryTransfers",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_CompanyId_SourceEstablishmentId_Business~",
                table: "InventoryTransfers",
                columns: new[] { "CompanyId", "SourceEstablishmentId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_CreatedByUserId",
                table: "InventoryTransfers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_DestinationEstablishmentId",
                table: "InventoryTransfers",
                column: "DestinationEstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_SourceEstablishmentId",
                table: "InventoryTransfers",
                column: "SourceEstablishmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryTransferItems");

            migrationBuilder.DropTable(
                name: "InventoryTransfers");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5, 6, 7, 8)");
        }
    }
}
