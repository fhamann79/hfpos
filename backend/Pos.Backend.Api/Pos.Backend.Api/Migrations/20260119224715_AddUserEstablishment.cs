using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEstablishment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EstablishmentId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_EstablishmentId",
                table: "Users",
                column: "EstablishmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Establishments_EstablishmentId",
                table: "Users",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Establishments_EstablishmentId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_EstablishmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EstablishmentId",
                table: "Users");
        }
    }
}
