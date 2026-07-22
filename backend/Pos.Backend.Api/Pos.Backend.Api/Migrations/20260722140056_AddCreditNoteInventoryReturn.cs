using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteInventoryReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.AddColumn<string>(
                name: "InventoryReturnNotes",
                table: "CreditNotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InventoryReturnedAt",
                table: "CreditNotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryReturnedByUserId",
                table: "CreditNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5, 6, 7, 8)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InventoryReturnedByUserId",
                table: "CreditNotes",
                column: "InventoryReturnedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNotes_InventoryReturnAuditComplete",
                table: "CreditNotes",
                sql: "((\"InventoryReturnedAt\" IS NULL AND \"InventoryReturnedByUserId\" IS NULL AND \"InventoryReturnNotes\" IS NULL) OR (\"InventoryReturnedAt\" IS NOT NULL AND \"InventoryReturnedByUserId\" IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_Users_InventoryReturnedByUserId",
                table: "CreditNotes",
                column: "InventoryReturnedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_Users_InventoryReturnedByUserId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_InventoryReturnedByUserId",
                table: "CreditNotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNotes_InventoryReturnAuditComplete",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "InventoryReturnNotes",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "InventoryReturnedAt",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "InventoryReturnedByUserId",
                table: "CreditNotes");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceType_SourceId_SourceLineId",
                table: "InventoryMovements",
                columns: new[] { "SourceType", "SourceId", "SourceLineId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"SourceLineId\" IS NOT NULL AND \"SourceType\" IN (4, 5, 6, 7)");
        }
    }
}
