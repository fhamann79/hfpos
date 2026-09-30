using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSaasPlatformControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Companies", "Users" IN SHARE ROW EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Companies" GROUP BY "Ruc" HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'BE-FE-518: duplicate Companies.Ruc; resolve legacy duplicates manually before migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "Users" GROUP BY "Username" HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'BE-FE-518: duplicate Users.Username; resolve legacy duplicates manually before migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "Users" GROUP BY "Email" HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'BE-FE-518: duplicate Users.Email; resolve legacy duplicates manually before migration';
                    END IF;
                END $$;
                """);
            migrationBuilder.CreateTable(
                name: "PlatformUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SessionVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUsers", x => x.Id);
                    table.CheckConstraint("CK_PlatformUsers_SessionVersion", "\"SessionVersion\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "PlatformTenantEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PlatformUserId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProvisioningSnapshot = table.Column<string>(type: "text", nullable: true),
                    InitialAdminUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformTenantEvents", x => x.Id);
                    table.CheckConstraint("CK_PlatformTenantEvents_Type", "(\"EventType\" = 'Provisioned' AND \"RequestId\" IS NOT NULL AND \"InitialAdminUserId\" IS NOT NULL AND \"ProvisioningSnapshot\" IS NOT NULL) OR (\"EventType\" IN ('Suspended', 'Reactivated') AND \"RequestId\" IS NULL AND \"InitialAdminUserId\" IS NULL AND \"ProvisioningSnapshot\" IS NULL AND \"Reason\" IS NOT NULL AND length(trim(\"Reason\")) > 0)");
                    table.ForeignKey(
                        name: "FK_PlatformTenantEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformTenantEvents_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformTenantEvents_Users_InitialAdminUserId",
                        column: x => x.InitialAdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Ruc",
                table: "Companies",
                column: "Ruc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformTenantEvents_CompanyId_CreatedAt_Id",
                table: "PlatformTenantEvents",
                columns: new[] { "CompanyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformTenantEvents_InitialAdminUserId",
                table: "PlatformTenantEvents",
                column: "InitialAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformTenantEvents_PlatformUserId",
                table: "PlatformTenantEvents",
                column: "PlatformUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformTenantEvents_RequestId",
                table: "PlatformTenantEvents",
                column: "RequestId",
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUsers_Email",
                table: "PlatformUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUsers_Username",
                table: "PlatformUsers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformTenantEvents");

            migrationBuilder.DropTable(
                name: "PlatformUsers");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Companies_Ruc",
                table: "Companies");
        }
    }
}
