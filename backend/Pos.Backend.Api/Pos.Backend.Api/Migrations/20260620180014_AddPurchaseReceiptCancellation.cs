using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "PurchaseReceipts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "PurchaseReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CanceledByUserId",
                table: "PurchaseReceipts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CanceledByUserId",
                table: "PurchaseReceipts",
                column: "CanceledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_Status_ReceiptDate",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "Status", "ReceiptDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5, 6, 7)");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReceipts_Users_CanceledByUserId",
                table: "PurchaseReceipts",
                column: "CanceledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReceipts_Users_CanceledByUserId",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceipts_CanceledByUserId",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceipts_CompanyId_Status_ReceiptDate",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "CanceledByUserId",
                table: "PurchaseReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5, 6)");
        }
    }
}
