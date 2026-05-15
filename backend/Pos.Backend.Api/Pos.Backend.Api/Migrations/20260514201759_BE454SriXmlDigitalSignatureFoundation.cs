using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class BE454SriXmlDigitalSignatureFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SriSignatureHash",
                table: "Sales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SriSignedAt",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSignedXml",
                table: "Sales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSigningCertificateSerialNumber",
                table: "Sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSigningCertificateSubject",
                table: "Sales",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSigningCertificateThumbprint",
                table: "Sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SriSignatureHash",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriSignedAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriSignedXml",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriSigningCertificateSerialNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriSigningCertificateSubject",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriSigningCertificateThumbprint",
                table: "Sales");
        }
    }
}
