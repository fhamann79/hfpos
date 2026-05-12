using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class BE451SriAccessKeyXmlDraftFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SriEmissionType",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SriEnvironment",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriNumericCode",
                table: "Sales",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriXmlDraft",
                table: "Sales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SriXmlGeneratedAt",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SriEmissionType",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriEnvironment",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriNumericCode",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriXmlDraft",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriXmlGeneratedAt",
                table: "Sales");
        }
    }
}
