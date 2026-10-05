using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistedPasswordRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Code", "Description", "IsActive", "CreatedAt")
                VALUES ('USERS_RECOVERY_MANAGE', 'Gestionar recuperacion asistida de usuarios', true, now())
                ON CONFLICT ("Code") DO NOTHING;
                WITH granted AS (
                    INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
                    SELECT r."Id", p."Id" FROM "Roles" r CROSS JOIN "Permissions" p
                    WHERE r."Code" = 'ADMIN' AND p."Code" = 'USERS_RECOVERY_MANAGE'
                    ON CONFLICT DO NOTHING RETURNING "RoleId"
                )
                UPDATE "Roles" SET "AuthorizationVersion" = "AuthorizationVersion" + 1
                WHERE "Id" IN (SELECT "RoleId" FROM granted);
                """);
            migrationBuilder.CreateTable(
                name: "PasswordRecoveryChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPlatform = table.Column<bool>(type: "boolean", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    PlatformUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    PasswordFingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    SessionVersion = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: true),
                    RoleVersion = table.Column<long>(type: "bigint", nullable: true),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: true),
                    EmissionPointId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DeliveryReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordRecoveryChallenges", x => x.Id);
                    table.CheckConstraint("CK_PasswordRecoveryChallenges_Bounds", "octet_length(\"TokenHash\") = 32 AND octet_length(\"PasswordFingerprint\") = 32 AND \"SessionVersion\" > 0 AND \"AttemptCount\" >= 0 AND \"ExpiresAt\" > \"CreatedAt\" AND length(trim(\"Reason\")) > 0 AND length(trim(\"DeliveryReference\")) > 0 AND NOT (\"ConsumedAt\" IS NOT NULL AND \"RevokedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_PasswordRecoveryChallenges_Scope", "(\"IsPlatform\" AND \"CompanyId\" IS NULL AND \"UserId\" IS NULL AND \"PlatformUserId\" IS NOT NULL AND \"RoleId\" IS NULL AND \"RoleVersion\" IS NULL AND \"EstablishmentId\" IS NULL AND \"EmissionPointId\" IS NULL) OR (NOT \"IsPlatform\" AND \"CompanyId\" IS NOT NULL AND \"UserId\" IS NOT NULL AND \"PlatformUserId\" IS NULL AND \"RoleId\" IS NOT NULL AND \"RoleVersion\" IS NOT NULL AND \"EstablishmentId\" IS NOT NULL AND \"EmissionPointId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PasswordRecoveryChallenges_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PasswordRecoveryChallenges_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PasswordRecoveryChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PasswordSecurityAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsPlatform = table.Column<bool>(type: "boolean", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: true),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    TargetId = table.Column<int>(type: "integer", nullable: true),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Event = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DeliveryReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordSecurityAudits", x => x.Id);
                    table.CheckConstraint("CK_PasswordSecurityAudits_Scope", "NOT \"IsPlatform\" OR \"CompanyId\" IS NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveryChallenges_CompanyId_UserId_CreatedAt",
                table: "PasswordRecoveryChallenges",
                columns: new[] { "CompanyId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveryChallenges_PlatformUserId_CreatedAt",
                table: "PasswordRecoveryChallenges",
                columns: new[] { "PlatformUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveryChallenges_UserId",
                table: "PasswordRecoveryChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordSecurityAudits_CompanyId_CreatedAt",
                table: "PasswordSecurityAudits",
                columns: new[] { "CompanyId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "AuthorizationVersion" = "AuthorizationVersion" + 1
                WHERE "Id" IN (SELECT rp."RoleId" FROM "RolePermissions" rp JOIN "Permissions" p ON p."Id" = rp."PermissionId"
                    WHERE p."Code" = 'USERS_RECOVERY_MANAGE');
                DELETE FROM "Permissions" WHERE "Code" = 'USERS_RECOVERY_MANAGE';
                """);
            migrationBuilder.DropTable(
                name: "PasswordRecoveryChallenges");

            migrationBuilder.DropTable(
                name: "PasswordSecurityAudits");
        }
    }
}
