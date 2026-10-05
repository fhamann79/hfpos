using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCriticalOperationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestEmissionPointId",
                table: "PurchaseReceipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "PurchaseReceipts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "PurchaseReceipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestEmissionPointId",
                table: "InventoryMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "InventoryMovements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "InventoryMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "CashSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "CashSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "CashMovements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "CashMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_RequestId",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseReceipt_Request",
                table: "PurchaseReceipts",
                sql: "(\"RequestId\" IS NULL AND \"RequestHash\" IS NULL AND \"RequestEmissionPointId\" IS NULL) OR (\"RequestId\" IS NOT NULL AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestHash\" IS NOT NULL AND \"RequestHash\" ~ '^[0-9A-F]{64}$' AND \"RequestEmissionPointId\" IS NOT NULL AND \"RequestEmissionPointId\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_RequestId",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovement_Request",
                table: "InventoryMovements",
                sql: "(\"RequestId\" IS NULL AND \"RequestHash\" IS NULL AND \"RequestEmissionPointId\" IS NULL) OR (\"RequestId\" IS NOT NULL AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestHash\" IS NOT NULL AND \"RequestHash\" ~ '^[0-9A-F]{64}$' AND \"RequestEmissionPointId\" IS NOT NULL AND \"RequestEmissionPointId\" > 0 AND \"SourceType\" IN (1, 2, 3))");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_CompanyId_RequestId",
                table: "CashSessions",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashSession_Request",
                table: "CashSessions",
                sql: "(\"RequestId\" IS NULL AND \"RequestHash\" IS NULL) OR (\"RequestId\" IS NOT NULL AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestHash\" IS NOT NULL AND \"RequestHash\" ~ '^[0-9A-F]{64}$')");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CompanyId_RequestId",
                table: "CashMovements",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashMovement_Request",
                table: "CashMovements",
                sql: "(\"RequestId\" IS NULL AND \"RequestHash\" IS NULL) OR (\"RequestId\" IS NOT NULL AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestHash\" IS NOT NULL AND \"RequestHash\" ~ '^[0-9A-F]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceipts_CompanyId_RequestId",
                table: "PurchaseReceipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseReceipt_Request",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId_RequestId",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovement_Request",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_CompanyId_RequestId",
                table: "CashSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashSession_Request",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CompanyId_RequestId",
                table: "CashMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashMovement_Request",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "RequestEmissionPointId",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "RequestEmissionPointId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "CashMovements");
        }
    }
}
