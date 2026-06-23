using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessDateSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "Sales",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneIdSnapshot",
                table: "Sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Guayaquil");

            migrationBuilder.AddColumn<DateOnly>(
                name: "CanceledBusinessDate",
                table: "PurchaseReceipts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledTimeZoneIdSnapshot",
                table: "PurchaseReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReceiptBusinessDate",
                table: "PurchaseReceipts",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptTimeZoneIdSnapshot",
                table: "PurchaseReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Guayaquil");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "InventoryMovements",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneIdSnapshot",
                table: "InventoryMovements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Guayaquil");

            migrationBuilder.Sql("""
                UPDATE "Sales"
                SET "BusinessDate" = ("CreatedAt" AT TIME ZONE 'America/Guayaquil')::date,
                    "TimeZoneIdSnapshot" = 'America/Guayaquil';
                """);

            migrationBuilder.Sql("""
                UPDATE "PurchaseReceipts"
                SET "ReceiptBusinessDate" = ("ReceiptDate" AT TIME ZONE 'UTC')::date,
                    "ReceiptTimeZoneIdSnapshot" = 'America/Guayaquil',
                    "CanceledBusinessDate" = CASE
                        WHEN "CanceledAt" IS NULL THEN NULL
                        ELSE ("CanceledAt" AT TIME ZONE 'America/Guayaquil')::date
                    END,
                    "CanceledTimeZoneIdSnapshot" = CASE
                        WHEN "CanceledAt" IS NULL THEN NULL
                        ELSE 'America/Guayaquil'
                    END;
                """);

            migrationBuilder.Sql("""
                UPDATE "InventoryMovements"
                SET "BusinessDate" = ("CreatedAt" AT TIME ZONE 'America/Guayaquil')::date,
                    "TimeZoneIdSnapshot" = 'America/Guayaquil';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_BusinessDate",
                table: "Sales",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_EstablishmentId_ReceiptBusinessD~",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "EstablishmentId", "ReceiptBusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_Status_CanceledBusinessDate",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "Status", "CanceledBusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_Status_ReceiptBusinessDate",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "Status", "ReceiptBusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_BusinessDate",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "EstablishmentId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_ProductId_Busi~",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "EstablishmentId", "ProductId", "BusinessDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_BusinessDate",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceipts_CompanyId_EstablishmentId_ReceiptBusinessD~",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceipts_CompanyId_Status_CanceledBusinessDate",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceipts_CompanyId_Status_ReceiptBusinessDate",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_BusinessDate",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId_EstablishmentId_ProductId_Busi~",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TimeZoneIdSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CanceledBusinessDate",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "CanceledTimeZoneIdSnapshot",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "ReceiptBusinessDate",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "ReceiptTimeZoneIdSnapshot",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "TimeZoneIdSnapshot",
                table: "InventoryMovements");
        }
    }
}
