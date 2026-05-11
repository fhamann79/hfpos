using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class BE450SriDocumentNumberingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessKey",
                table: "Sales",
                type: "character varying(49)",
                maxLength: 49,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationNumber",
                table: "Sales",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorizedAt",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentIssuedAt",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentStatus",
                table: "Sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmissionPointCodeSnapshot",
                table: "Sales",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstablishmentCodeSnapshot",
                table: "Sales",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sequential",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    EmissionPointId = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    CurrentNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentSequences_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentSequences_EmissionPoints_EmissionPointId",
                        column: x => x.EmissionPointId,
                        principalTable: "EmissionPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentSequences_Establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_DocumentTyp~",
                table: "Sales",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "DocumentType", "Sequential" },
                unique: true,
                filter: "\"Sequential\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_DocumentStatus",
                table: "Sales",
                column: "DocumentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_Number",
                table: "Sales",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSequences_CompanyId_EstablishmentId_EmissionPointId~",
                table: "DocumentSequences",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSequences_EmissionPointId",
                table: "DocumentSequences",
                column: "EmissionPointId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSequences_EstablishmentId",
                table: "DocumentSequences",
                column: "EstablishmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSequences");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_DocumentTyp~",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_DocumentStatus",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_Number",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AccessKey",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AuthorizationNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AuthorizedAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DocumentIssuedAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DocumentStatus",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "EmissionPointCodeSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "EstablishmentCodeSnapshot",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Sequential",
                table: "Sales");
        }
    }
}
