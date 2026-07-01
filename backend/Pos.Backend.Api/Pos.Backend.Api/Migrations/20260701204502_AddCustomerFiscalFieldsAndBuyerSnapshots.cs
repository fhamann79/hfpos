using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerFiscalFieldsAndBuyerSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerAddressSnapshot",
                table: "Sales",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerEmailSnapshot",
                table: "Sales",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerIdentificationSnapshot",
                table: "Sales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerIdentificationTypeSnapshot",
                table: "Sales",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerNameSnapshot",
                table: "Sales",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Customers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificationType",
                table: "Customers",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Customers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_Identification",
                table: "Customers",
                columns: new[] { "CompanyId", "Identification" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId_Identification",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BuyerAddressSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BuyerEmailSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BuyerIdentificationSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BuyerIdentificationTypeSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BuyerNameSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IdentificationType",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Customers");
        }
    }
}
