using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteSriSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SriSignatureHash",
                table: "CreditNotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SriSignedAt",
                table: "CreditNotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSignedXml",
                table: "CreditNotes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSigningCertificateSerialNumber",
                table: "CreditNotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSigningCertificateSubject",
                table: "CreditNotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriSigningCertificateThumbprint",
                table: "CreditNotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SriSignatureHash",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "SriSignedAt",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "SriSignedXml",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "SriSigningCertificateSerialNumber",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "SriSigningCertificateSubject",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "SriSigningCertificateThumbprint",
                table: "CreditNotes");
        }
    }
}
