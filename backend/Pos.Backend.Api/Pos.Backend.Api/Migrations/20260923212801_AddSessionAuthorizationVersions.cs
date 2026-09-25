using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionAuthorizationVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SessionVersion",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "AuthorizationVersion",
                table: "Roles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_SessionVersion_Positive",
                table: "Users",
                sql: "\"SessionVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Roles_AuthorizationVersion_Positive",
                table: "Roles",
                sql: "\"AuthorizationVersion\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_SessionVersion_Positive",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Roles_AuthorizationVersion_Positive",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AuthorizationVersion",
                table: "Roles");
        }
    }
}
