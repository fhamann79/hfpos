using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoverableSaleCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashChange",
                table: "Sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CashReceived",
                table: "Sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Sales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "SaleItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductSkuSnapshot",
                table: "SaleItems",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CompanyId_RequestId",
                table: "Sales",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_CashReceived",
                table: "Sales",
                sql: "(\"RequestId\" IS NULL) OR (\"PaymentMethod\" = 0 AND \"CashReceived\" IS NOT NULL AND \"CashChange\" IS NOT NULL AND \"CashReceived\" >= \"Total\" AND \"CashChange\" = \"CashReceived\" - \"Total\") OR (\"PaymentMethod\" <> 0 AND \"CashReceived\" IS NULL AND \"CashChange\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_RequestIdentity",
                table: "Sales",
                sql: "(\"RequestId\" IS NULL AND \"RequestHash\" IS NULL) OR (\"RequestId\" IS NOT NULL AND \"RequestHash\" IS NOT NULL AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(\"RequestHash\") = 64)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_CompanyId_RequestId",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_CashReceived",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_RequestIdentity",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CashChange",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CashReceived",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ProductNameSnapshot",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "ProductSkuSnapshot",
                table: "SaleItems");
        }
    }
}
