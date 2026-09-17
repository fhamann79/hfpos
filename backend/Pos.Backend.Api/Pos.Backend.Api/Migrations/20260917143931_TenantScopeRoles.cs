using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class TenantScopeRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_RoleId",
                table: "Users");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Roles",
                type: "integer",
                nullable: true);

            // EF runs the migration transactionally. Preserve originals until every mapping is verified.
            migrationBuilder.Sql("""
                LOCK TABLE "Companies", "Roles", "RolePermissions", "Users" IN SHARE ROW EXCLUSIVE MODE;

                DO $tenant_scope_roles$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Roles" GROUP BY "Code" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'TenantScopeRoles: duplicate global role codes require explicit reconciliation before migration';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Users" u
                        LEFT JOIN "Companies" c ON c."Id" = u."CompanyId"
                        LEFT JOIN "Roles" r ON r."Id" = u."RoleId"
                        WHERE c."Id" IS NULL OR r."Id" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'TenantScopeRoles: invalid user company or role reference';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Users" u
                        LEFT JOIN "Establishments" e ON e."Id" = u."EstablishmentId"
                        LEFT JOIN "EmissionPoints" ep ON ep."Id" = u."EmissionPointId"
                        WHERE e."CompanyId" IS DISTINCT FROM u."CompanyId"
                            OR ep."EstablishmentId" IS DISTINCT FROM u."EstablishmentId"
                    ) THEN
                        RAISE EXCEPTION 'TenantScopeRoles: inconsistent user operational assignments require explicit reconciliation';
                    END IF;
                END; $tenant_scope_roles$;

                CREATE TEMP TABLE "_OriginalRoles" ON COMMIT DROP AS
                    SELECT "Id", "Code", "Name", "IsActive", "CreatedAt" FROM "Roles";
                CREATE TEMP TABLE "_OriginalRolePermissions" ON COMMIT DROP AS
                    SELECT "RoleId", "PermissionId" FROM "RolePermissions";
                CREATE TEMP TABLE "_OriginalUserRoles" ON COMMIT DROP AS
                    SELECT "Id", "CompanyId", "RoleId" FROM "Users";

                INSERT INTO "Roles" ("CompanyId", "Code", "Name", "IsActive", "CreatedAt")
                    SELECT c."Id", r."Code", r."Name", r."IsActive", r."CreatedAt"
                    FROM "Companies" c CROSS JOIN "_OriginalRoles" r
                    ORDER BY c."Id", r."Id";

                INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
                    SELECT tenant."Id", rp."PermissionId"
                    FROM "_OriginalRolePermissions" rp
                    JOIN "_OriginalRoles" original ON original."Id" = rp."RoleId"
                    JOIN "Roles" tenant ON tenant."Code" = original."Code"
                        AND tenant."CompanyId" IS NOT NULL;

                UPDATE "Users" u SET "RoleId" = tenant."Id"
                    FROM "_OriginalRoles" original, "Roles" tenant
                    WHERE u."RoleId" = original."Id"
                        AND tenant."Code" = original."Code"
                        AND tenant."CompanyId" = u."CompanyId";

                DO $tenant_scope_roles$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "_OriginalUserRoles" before
                        JOIN "_OriginalRoles" original ON original."Id" = before."RoleId"
                        LEFT JOIN "Users" u ON u."Id" = before."Id"
                        LEFT JOIN "Roles" r ON r."Id" = u."RoleId"
                        WHERE u."Id" IS NULL OR u."CompanyId" <> before."CompanyId"
                            OR r."CompanyId" IS DISTINCT FROM u."CompanyId"
                            OR r."Code" IS DISTINCT FROM original."Code"
                    ) THEN
                        RAISE EXCEPTION 'TenantScopeRoles: user remapping verification failed';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Companies" c CROSS JOIN "_OriginalRoles" original
                        LEFT JOIN "Roles" tenant ON tenant."CompanyId" = c."Id"
                            AND tenant."Code" = original."Code"
                        WHERE tenant."Id" IS NULL
                            OR tenant."Name" IS DISTINCT FROM original."Name"
                            OR tenant."IsActive" IS DISTINCT FROM original."IsActive"
                            OR tenant."CreatedAt" IS DISTINCT FROM original."CreatedAt"
                            OR ARRAY(SELECT "PermissionId" FROM "RolePermissions"
                                WHERE "RoleId" = tenant."Id" ORDER BY "PermissionId")
                            IS DISTINCT FROM ARRAY(SELECT "PermissionId" FROM "_OriginalRolePermissions"
                                WHERE "RoleId" = original."Id" ORDER BY "PermissionId")
                    ) THEN
                        RAISE EXCEPTION 'TenantScopeRoles: role or permission preservation verification failed';
                    END IF;
                END; $tenant_scope_roles$;

                DELETE FROM "RolePermissions" WHERE "RoleId" IN (SELECT "Id" FROM "_OriginalRoles");
                DELETE FROM "Roles" WHERE "Id" IN (SELECT "Id" FROM "_OriginalRoles");
                """);

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "Roles",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Roles_CompanyId_Id",
                table: "Roles",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId_RoleId",
                table: "Users",
                columns: new[] { "CompanyId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_CompanyId",
                table: "Roles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_CompanyId_Code",
                table: "Roles",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Companies_CompanyId",
                table: "Roles",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Roles_CompanyId_RoleId",
                table: "Users",
                columns: new[] { "CompanyId", "RoleId" },
                principalTable: "Roles",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Roles_Companies_CompanyId",
                table: "Roles");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_CompanyId_RoleId",
                table: "Users");

            // Rollback must never union permissions or silently choose between diverged tenant roles.
            migrationBuilder.Sql("""
                LOCK TABLE "Companies", "Roles", "RolePermissions", "Users" IN SHARE ROW EXCLUSIVE MODE;
                DO $tenant_scope_roles$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "Roles" a JOIN "Roles" b ON a."Code" = b."Code" AND a."Id" < b."Id"
                        WHERE a."Name" IS DISTINCT FROM b."Name"
                            OR a."IsActive" IS DISTINCT FROM b."IsActive"
                            OR a."CreatedAt" IS DISTINCT FROM b."CreatedAt"
                            OR ARRAY(SELECT "PermissionId" FROM "RolePermissions"
                                WHERE "RoleId" = a."Id" ORDER BY "PermissionId")
                            IS DISTINCT FROM ARRAY(SELECT "PermissionId" FROM "RolePermissions"
                                WHERE "RoleId" = b."Id" ORDER BY "PermissionId")
                    ) THEN
                        RAISE EXCEPTION 'TenantScopeRoles rollback refused: tenant roles have diverged; reconcile explicitly or restore a pre-migration backup';
                    END IF;
                END; $tenant_scope_roles$;

                CREATE TEMP TABLE "_GlobalRoleMap" ON COMMIT DROP AS
                    SELECT "Id", MIN("Id") OVER (PARTITION BY "Code") AS "GlobalId" FROM "Roles";
                UPDATE "Users" u SET "RoleId" = m."GlobalId"
                    FROM "_GlobalRoleMap" m WHERE u."RoleId" = m."Id";
                DELETE FROM "RolePermissions" WHERE "RoleId" IN
                    (SELECT "Id" FROM "_GlobalRoleMap" WHERE "Id" <> "GlobalId");
                DELETE FROM "Roles" WHERE "Id" IN
                    (SELECT "Id" FROM "_GlobalRoleMap" WHERE "Id" <> "GlobalId");
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId_RoleId",
                table: "Users");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Roles_CompanyId_Id",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_CompanyId",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_CompanyId_Code",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Roles");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId",
                table: "Users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
