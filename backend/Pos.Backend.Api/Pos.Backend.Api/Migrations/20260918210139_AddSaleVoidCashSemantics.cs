using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleVoidCashSemantics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "VoidBusinessDate",
                table: "Sales",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidCashEffect",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidCashMovementId",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidCashSessionId",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Sales",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidTimeZoneIdSnapshot",
                table: "Sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_VoidCashMovementId",
                table: "Sales",
                column: "VoidCashMovementId",
                unique: true,
                filter: "\"VoidCashMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_VoidCashSessionId",
                table: "Sales",
                column: "VoidCashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_VoidedByUserId",
                table: "Sales",
                column: "VoidedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_VoidAuditComplete",
                table: "Sales",
                sql: "(\"VoidCashEffect\" IS NULL OR (\"Status\" = 2 AND \"VoidedAt\" IS NOT NULL AND \"VoidedByUserId\" IS NOT NULL AND \"VoidReason\" IS NOT NULL AND \"VoidBusinessDate\" IS NOT NULL AND \"VoidTimeZoneIdSnapshot\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_VoidCashLinks",
                table: "Sales",
                sql: "((\"VoidCashEffect\" IS NULL AND \"VoidCashSessionId\" IS NULL AND \"VoidCashMovementId\" IS NULL) OR (\"VoidCashEffect\" = 1 AND \"VoidCashSessionId\" IS NOT NULL AND \"VoidCashMovementId\" IS NULL) OR (\"VoidCashEffect\" = 2 AND \"VoidCashSessionId\" IS NOT NULL AND \"VoidCashMovementId\" IS NOT NULL) OR (\"VoidCashEffect\" = 3 AND \"VoidCashSessionId\" IS NULL AND \"VoidCashMovementId\" IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_CashMovements_VoidCashMovementId",
                table: "Sales",
                column: "VoidCashMovementId",
                principalTable: "CashMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_CashSessions_VoidCashSessionId",
                table: "Sales",
                column: "VoidCashSessionId",
                principalTable: "CashSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Users_VoidedByUserId",
                table: "Sales",
                column: "VoidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_CashMovements_VoidCashMovementId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_CashSessions_VoidCashSessionId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Users_VoidedByUserId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_VoidCashMovementId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_VoidCashSessionId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_VoidedByUserId",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_VoidAuditComplete",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_VoidCashLinks",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VoidBusinessDate",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VoidCashEffect",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VoidCashMovementId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VoidCashSessionId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VoidTimeZoneIdSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "Sales");
        }
    }
}
