using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class BE455SriReceptionAuthorizationSandboxPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SriAuthorizationStatus",
                table: "Sales",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SriLastCheckedAt",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriLastSubmissionError",
                table: "Sales",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SriReceptionStatus",
                table: "Sales",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SriSubmittedAt",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SriSubmissionAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SaleId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    EmissionPointId = table.Column<int>(type: "integer", nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(49)", maxLength: 49, nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    AttemptType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReceptionStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AuthorizationStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AuthorizationNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AuthorizationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestXmlSnapshot = table.Column<string>(type: "text", nullable: true),
                    ResponseXml = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SriMessageIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SriMessageType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SriMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SriAdditionalInfo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SriSubmissionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SriSubmissionAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SriSubmissionAttempts_EmissionPoints_EmissionPointId",
                        column: x => x.EmissionPointId,
                        principalTable: "EmissionPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SriSubmissionAttempts_Establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SriSubmissionAttempts_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SriSubmissionAttempts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_AccessKey",
                table: "SriSubmissionAttempts",
                column: "AccessKey");

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_CompanyId_AccessKey",
                table: "SriSubmissionAttempts",
                columns: new[] { "CompanyId", "AccessKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_CompanyId_CreatedAt",
                table: "SriSubmissionAttempts",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_CreatedByUserId",
                table: "SriSubmissionAttempts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_EmissionPointId",
                table: "SriSubmissionAttempts",
                column: "EmissionPointId");

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_EstablishmentId",
                table: "SriSubmissionAttempts",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_SaleId_CreatedAt",
                table: "SriSubmissionAttempts",
                columns: new[] { "SaleId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "SriAuthorizationStatus",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriLastCheckedAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriLastSubmissionError",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriReceptionStatus",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SriSubmittedAt",
                table: "Sales");
        }
    }
}
