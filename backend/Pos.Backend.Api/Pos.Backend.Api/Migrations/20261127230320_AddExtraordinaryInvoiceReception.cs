using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExtraordinaryInvoiceReception : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveryCompletedAt",
                table: "SriSubmissionAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryReason",
                table: "SriSubmissionAttempts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecoveryEvidenceId",
                table: "SriSubmissionAttempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryReference",
                table: "SriSubmissionAttempts",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoverySignedXmlHash",
                table: "SriSubmissionAttempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_CompanyId_SaleId",
                table: "SriSubmissionAttempts",
                columns: new[] { "CompanyId", "SaleId" },
                unique: true,
                filter: "\"AttemptType\" = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SriSubmissionAttempts_ExtraordinaryReception",
                table: "SriSubmissionAttempts",
                sql: "\"AttemptType\" <> 3 OR (\"SaleId\" IS NOT NULL AND \"RecoveryEvidenceId\" IS NOT NULL AND \"RecoveryReason\" IS NOT NULL AND \"RecoverySignedXmlHash\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "SriSubmissionAttempts" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "SriSubmissionAttempts" WHERE "AttemptType" IN (3, 4)) THEN
                        RAISE EXCEPTION 'Extraordinary fiscal reception history prevents migration rollback';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_SriSubmissionAttempts_CompanyId_SaleId",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SriSubmissionAttempts_ExtraordinaryReception",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "RecoveryCompletedAt",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "RecoveryEvidenceId",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "RecoveryReason",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "RecoveryReference",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "RecoverySignedXmlHash",
                table: "SriSubmissionAttempts");
        }
    }
}
