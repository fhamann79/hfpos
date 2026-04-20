using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "Roles" ("Code", "Name", "IsActive", "CreatedAt")
                SELECT 'ADMIN', 'Administrador', TRUE, NOW()
                WHERE NOT EXISTS (
                    SELECT 1 FROM "Roles" WHERE "Code" = 'ADMIN'
                );

                INSERT INTO "Roles" ("Code", "Name", "IsActive", "CreatedAt")
                SELECT 'SUPERVISOR', 'Supervisor', TRUE, NOW()
                WHERE NOT EXISTS (
                    SELECT 1 FROM "Roles" WHERE "Code" = 'SUPERVISOR'
                );

                INSERT INTO "Roles" ("Code", "Name", "IsActive", "CreatedAt")
                SELECT 'CASHIER', 'Cajero', TRUE, NOW()
                WHERE NOT EXISTS (
                    SELECT 1 FROM "Roles" WHERE "Code" = 'CASHIER'
                );
                """);

            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "RoleId" = (
                    SELECT "Id"
                    FROM "Roles"
                    WHERE "Code" = 'ADMIN'
                    ORDER BY "Id"
                    LIMIT 1
                )
                WHERE "RoleId" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "RoleId",
                table: "Users",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_RoleId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
