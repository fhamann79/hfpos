using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAutonomousElectronicIssuing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FiscalDelegationId",
                table: "SriSubmissionAttempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticProcessingEnabled",
                table: "CompanySriSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "AutomaticProcessingRevision",
                table: "CompanySriSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "FiscalDelegationAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    PreviousEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    PlatformUserId = table.Column<int>(type: "integer", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalDelegationAudits", x => x.Id);
                    table.CheckConstraint("CK_FiscalDelegationAudits_Actor", "(\"UserId\" IS NOT NULL) <> (\"PlatformUserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FiscalDelegationAudits_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDelegationAudits_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDelegationAudits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FiscalDelegations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    EnabledByUserId = table.Column<int>(type: "integer", nullable: false),
                    AuthorizerEstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    AuthorizerEmissionPointId = table.Column<int>(type: "integer", nullable: false),
                    EnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisabledByUserId = table.Column<int>(type: "integer", nullable: true),
                    DisabledByPlatformUserId = table.Column<int>(type: "integer", nullable: true),
                    DisabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalDelegations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalDelegations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDelegations_EmissionPoints_AuthorizerEmissionPointId",
                        column: x => x.AuthorizerEmissionPointId,
                        principalTable: "EmissionPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDelegations_Establishments_AuthorizerEstablishmentId",
                        column: x => x.AuthorizerEstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDelegations_PlatformUsers_DisabledByPlatformUserId",
                        column: x => x.DisabledByPlatformUserId,
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDelegations_Users_DisabledByUserId",
                        column: x => x.DisabledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDelegations_Users_EnabledByUserId",
                        column: x => x.EnabledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ElectronicIssuingJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    EmissionPointId = table.Column<int>(type: "integer", nullable: false),
                    SaleId = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    FiscalDelegationId = table.Column<long>(type: "bigint", nullable: true),
                    AccessKey = table.Column<string>(type: "character varying(49)", maxLength: 49, nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    DraftHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AutomaticRequested = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceptionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SafeError = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectronicIssuingJobs", x => x.Id);
                    table.CheckConstraint("CK_ElectronicIssuingJobs_Bounds", "\"AttemptCount\" >= 0 AND \"Fence\" >= 0 AND \"DocumentType\" = 1 AND \"State\" BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_ElectronicIssuingJobs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicIssuingJobs_EmissionPoints_EmissionPointId",
                        column: x => x.EmissionPointId,
                        principalTable: "EmissionPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicIssuingJobs_Establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicIssuingJobs_FiscalDelegations_FiscalDelegationId",
                        column: x => x.FiscalDelegationId,
                        principalTable: "FiscalDelegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicIssuingJobs_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicIssuingJobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_FiscalDelegationId",
                table: "SriSubmissionAttempts",
                column: "FiscalDelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicIssuingJobs_AutomaticRequested_State_NextAttemptA~",
                table: "ElectronicIssuingJobs",
                columns: new[] { "AutomaticRequested", "State", "NextAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicIssuingJobs_CompanyId_EstablishmentId_EmissionPoi~",
                table: "ElectronicIssuingJobs",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicIssuingJobs_EmissionPointId",
                table: "ElectronicIssuingJobs",
                column: "EmissionPointId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicIssuingJobs_EstablishmentId",
                table: "ElectronicIssuingJobs",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicIssuingJobs_FiscalDelegationId",
                table: "ElectronicIssuingJobs",
                column: "FiscalDelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicIssuingJobs_SaleId",
                table: "ElectronicIssuingJobs",
                column: "SaleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicIssuingJobs_UserId",
                table: "ElectronicIssuingJobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegationAudits_CompanyId_Revision",
                table: "FiscalDelegationAudits",
                columns: new[] { "CompanyId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegationAudits_PlatformUserId",
                table: "FiscalDelegationAudits",
                column: "PlatformUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegationAudits_UserId",
                table: "FiscalDelegationAudits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegations_AuthorizerEmissionPointId",
                table: "FiscalDelegations",
                column: "AuthorizerEmissionPointId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegations_AuthorizerEstablishmentId",
                table: "FiscalDelegations",
                column: "AuthorizerEstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegations_CompanyId",
                table: "FiscalDelegations",
                column: "CompanyId",
                unique: true,
                filter: "\"DisabledAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegations_CompanyId_Revision",
                table: "FiscalDelegations",
                columns: new[] { "CompanyId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegations_DisabledByPlatformUserId",
                table: "FiscalDelegations",
                column: "DisabledByPlatformUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegations_DisabledByUserId",
                table: "FiscalDelegations",
                column: "DisabledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDelegations_EnabledByUserId",
                table: "FiscalDelegations",
                column: "EnabledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SriSubmissionAttempts_FiscalDelegations_FiscalDelegationId",
                table: "SriSubmissionAttempts",
                column: "FiscalDelegationId",
                principalTable: "FiscalDelegations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SriSubmissionAttempts_FiscalDelegations_FiscalDelegationId",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropTable(
                name: "ElectronicIssuingJobs");

            migrationBuilder.DropTable(
                name: "FiscalDelegationAudits");

            migrationBuilder.DropTable(
                name: "FiscalDelegations");

            migrationBuilder.DropIndex(
                name: "IX_SriSubmissionAttempts_FiscalDelegationId",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "FiscalDelegationId",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "AutomaticProcessingEnabled",
                table: "CompanySriSettings");

            migrationBuilder.DropColumn(
                name: "AutomaticProcessingRevision",
                table: "CompanySriSettings");
        }
    }
}
