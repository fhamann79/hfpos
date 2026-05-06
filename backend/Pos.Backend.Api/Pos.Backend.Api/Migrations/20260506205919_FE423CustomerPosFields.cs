using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class FE423CustomerPosFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Identification",
                table: "Customers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Customers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Identification",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Customers");
        }
    }
}
