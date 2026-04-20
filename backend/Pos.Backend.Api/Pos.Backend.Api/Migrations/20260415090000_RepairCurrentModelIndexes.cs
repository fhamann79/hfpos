using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pos.Backend.Api.Infrastructure.Data;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    [DbContext(typeof(PosDbContext))]
    [Migration("20260415090000_RepairCurrentModelIndexes")]
    public partial class RepairCurrentModelIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_Establishments_CompanyId",
                table: "Establishments");

            migrationBuilder.DropIndex(
                name: "IX_EmissionPoints_EstablishmentId",
                table: "EmissionPoints");

            migrationBuilder.CreateIndex(
                name: "IX_Establishments_CompanyId_Code",
                table: "Establishments",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmissionPoints_EstablishmentId_Code",
                table: "EmissionPoints",
                columns: new[] { "EstablishmentId", "Code" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Establishments_CompanyId_Code",
                table: "Establishments");

            migrationBuilder.DropIndex(
                name: "IX_EmissionPoints_EstablishmentId_Code",
                table: "EmissionPoints");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId",
                table: "InventoryMovements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Establishments_CompanyId",
                table: "Establishments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_EmissionPoints_EstablishmentId",
                table: "EmissionPoints",
                column: "EstablishmentId");
        }
    }
}
