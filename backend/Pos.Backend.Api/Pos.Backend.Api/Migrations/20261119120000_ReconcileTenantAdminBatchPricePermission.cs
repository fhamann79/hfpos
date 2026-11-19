using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    public partial class ReconcileTenantAdminBatchPricePermission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF owns the transaction and migration-history lock. Drain tenant administration
            // (FOR UPDATE), operational writers (FOR SHARE) and provisioning (INSERT) first.
            // Provisioning takes Companies -> Permissions -> Roles/RolePermissions. The
            // Companies gate also excludes EF role/grant writes regardless of their DML order.
            // These modes allow plain SELECTs throughout; this is not a substitute for the release freeze.
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';
                SET LOCAL statement_timeout = '25s';
                LOCK TABLE "Companies" IN EXCLUSIVE MODE;
                LOCK TABLE "Permissions", "Roles", "RolePermissions" IN SHARE ROW EXCLUSIVE MODE;
                """);

            // Freeze the policy literals from TenantDefaults at the time of this migration.
            migrationBuilder.Sql("""
                DO $dev532$
                DECLARE
                    batch_code constant text := 'CATALOG_PRODUCT_PRICES_BATCH';
                    fiscal_codes constant text[] := ARRAY[
                        'CATALOG_PRODUCTS_READ', 'CATALOG_PRODUCTS_WRITE', 'FISCAL_SETTINGS_READ', 'FISCAL_SETTINGS_WRITE'];
                    batch_id integer;
                    catalog_added integer := 0;
                    roles_changed bigint;
                    company_count bigint;
                    companies_without_admin bigint;
                    admin_count bigint;
                    fiscal_catalog_missing bigint;
                    fiscal_catalog_inactive bigint;
                    fiscal_admin_missing_grants bigint;
                    fiscal_status text;
                    summary text;
                BEGIN
                    IF (SELECT count(*) FROM "Permissions" WHERE "Code" = batch_code) > 1 THEN
                        RAISE EXCEPTION 'DEV532_BATCH_PERMISSION_DUPLICATE';
                    END IF;

                    SELECT "Id" INTO batch_id FROM "Permissions" WHERE "Code" = batch_code;
                    IF EXISTS (
                        SELECT 1 FROM "Permissions" WHERE "Id" = batch_id AND "IsActive" IS DISTINCT FROM TRUE
                    ) THEN
                        RAISE EXCEPTION 'DEV532_BATCH_PERMISSION_INACTIVE';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "Roles" r LEFT JOIN "Companies" c ON c."Id" = r."CompanyId"
                        WHERE r."Code" = 'ADMIN'
                          AND (c."Id" IS NULL OR r."AuthorizationVersion" IS NULL OR r."AuthorizationVersion" <= 0)
                    ) OR EXISTS (
                        SELECT 1 FROM "Roles" WHERE "Code" = 'ADMIN' GROUP BY "CompanyId" HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'DEV532_ADMIN_ROLE_INCONSISTENT';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "RolePermissions" rp LEFT JOIN "Roles" r ON r."Id" = rp."RoleId"
                        WHERE rp."PermissionId" = batch_id AND r."Code" IS DISTINCT FROM 'ADMIN'
                    ) THEN
                        RAISE EXCEPTION 'DEV532_BATCH_PERMISSION_UNEXPECTED_GRANT';
                    END IF;

                    IF batch_id IS NULL THEN
                        INSERT INTO "Permissions" ("Code", "Description", "IsActive", "CreatedAt")
                        VALUES (batch_code, 'Update catalog prices in batches', TRUE, now())
                        ON CONFLICT ("Code") DO NOTHING
                        RETURNING "Id" INTO STRICT batch_id;
                        catalog_added := 1;
                    END IF;

                    -- No activity filter: a suspended company or inactive ADMIN may later be reactivated.
                    WITH inserted AS (
                        INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
                        SELECT r."Id", batch_id
                        FROM "Roles" r JOIN "Companies" c ON c."Id" = r."CompanyId"
                        WHERE r."Code" = 'ADMIN'
                        ON CONFLICT ("RoleId", "PermissionId") DO NOTHING
                        RETURNING "RoleId"
                    )
                    UPDATE "Roles" r SET "AuthorizationVersion" = r."AuthorizationVersion" + 1
                    FROM inserted WHERE r."Id" = inserted."RoleId";
                    GET DIAGNOSTICS roles_changed = ROW_COUNT;

                    IF EXISTS (
                        SELECT 1 FROM "Roles" r WHERE r."Code" = 'ADMIN' AND NOT EXISTS (
                            SELECT 1 FROM "RolePermissions" rp
                            WHERE rp."RoleId" = r."Id" AND rp."PermissionId" = batch_id
                        )
                    ) THEN
                        RAISE EXCEPTION 'DEV532_BATCH_PERMISSION_COVERAGE_INCOMPLETE';
                    END IF;

                    SELECT count(*) INTO admin_count FROM "Roles" WHERE "Code" = 'ADMIN';
                    SELECT count(*), count(*) FILTER (WHERE r."Id" IS NULL)
                    INTO company_count, companies_without_admin
                    FROM "Companies" c LEFT JOIN "Roles" r ON r."CompanyId" = c."Id" AND r."Code" = 'ADMIN';

                    -- Report fiscal drift without changing policy, catalog entries or grants.
                    SELECT count(*) FILTER (WHERE p."Id" IS NULL),
                           count(*) FILTER (WHERE p."Id" IS NOT NULL AND p."IsActive" IS DISTINCT FROM TRUE)
                    INTO fiscal_catalog_missing, fiscal_catalog_inactive
                    FROM unnest(fiscal_codes) AS expected(code)
                    LEFT JOIN "Permissions" p ON p."Code" = expected.code;

                    SELECT count(*) INTO fiscal_admin_missing_grants
                    FROM "Roles" r CROSS JOIN unnest(fiscal_codes) AS expected(code)
                    WHERE r."Code" = 'ADMIN' AND NOT EXISTS (
                        SELECT 1 FROM "RolePermissions" rp JOIN "Permissions" p ON p."Id" = rp."PermissionId"
                        WHERE rp."RoleId" = r."Id" AND p."Code" = expected.code AND p."IsActive"
                    );

                    fiscal_status := CASE
                        WHEN company_count = 0 AND fiscal_catalog_missing = cardinality(fiscal_codes) THEN 'EMPTY_PRE_SEED'
                        WHEN fiscal_catalog_missing + fiscal_catalog_inactive + fiscal_admin_missing_grants
                            > 0 THEN 'REVIEW_REQUIRED'
                        ELSE 'OK'
                    END;
                    summary := format(
                        'DEV532_RBAC companies=%s companies_without_admin=%s admins=%s batch_catalog_added=%s new_grants=%s fiscal_status=%s fiscal_catalog_missing=%s fiscal_catalog_inactive=%s fiscal_admin_missing_grants=%s',
                        company_count, companies_without_admin, admin_count, catalog_added, roles_changed, fiscal_status,
                        fiscal_catalog_missing, fiscal_catalog_inactive, fiscal_admin_missing_grants);
                    -- The controlled EF wrapper suppresses its transcript. Keep aggregate evidence
                    -- in the PostgreSQL server log too; the summary is not proof of EF commit.
                    RAISE LOG '%', summary;
                    RAISE INFO '%', summary;
                END $dev532$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $dev532$
                BEGIN
                    RAISE EXCEPTION 'DEV532_RBAC_DOWNGRADE_BLOCKED';
                END $dev532$;
                """);
        }
    }
}
