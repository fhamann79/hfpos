using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProtectHistoricalMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Companies_CompanyId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanySriSettings_Companies_CompanyId",
                table: "CompanySriSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanySriSettings_Users_LastUpdatedByUserId",
                table: "CompanySriSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Companies_CompanyId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_Companies_CompanyId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_DocumentSequences_DocumentSequenceId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_EmissionPoints_EmissionPointId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_Establishments_EstablishmentId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_Users_UserId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequences_Companies_CompanyId",
                table: "DocumentSequences");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequences_EmissionPoints_EmissionPointId",
                table: "DocumentSequences");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequences_Establishments_EstablishmentId",
                table: "DocumentSequences");

            migrationBuilder.DropForeignKey(
                name: "FK_EmissionPoints_Establishments_EstablishmentId",
                table: "EmissionPoints");

            migrationBuilder.DropForeignKey(
                name: "FK_Establishments_Companies_CompanyId",
                table: "Establishments");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Companies_CompanyId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Establishments_EstablishmentId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Products_ProductId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Users_UserId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Companies_CompanyId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Companies_CompanyId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Establishments_EstablishmentId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Products_ProductId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Companies_CompanyId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Customers_CustomerId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_EmissionPoints_EmissionPointId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Establishments_EstablishmentId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Users_UserId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Companies_CompanyId",
                table: "Suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_EmissionPoints_EmissionPointId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Establishments_EstablishmentId",
                table: "Users");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Companies_CompanyId",
                table: "Categories",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySriSettings_Companies_CompanyId",
                table: "CompanySriSettings",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySriSettings_Users_LastUpdatedByUserId",
                table: "CompanySriSettings",
                column: "LastUpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Companies_CompanyId",
                table: "Customers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_Companies_CompanyId",
                table: "DocumentSequenceAudits",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_DocumentSequences_DocumentSequenceId",
                table: "DocumentSequenceAudits",
                column: "DocumentSequenceId",
                principalTable: "DocumentSequences",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_EmissionPoints_EmissionPointId",
                table: "DocumentSequenceAudits",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_Establishments_EstablishmentId",
                table: "DocumentSequenceAudits",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_Users_UserId",
                table: "DocumentSequenceAudits",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequences_Companies_CompanyId",
                table: "DocumentSequences",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequences_EmissionPoints_EmissionPointId",
                table: "DocumentSequences",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequences_Establishments_EstablishmentId",
                table: "DocumentSequences",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmissionPoints_Establishments_EstablishmentId",
                table: "EmissionPoints",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Establishments_Companies_CompanyId",
                table: "Establishments",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Companies_CompanyId",
                table: "InventoryMovements",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Establishments_EstablishmentId",
                table: "InventoryMovements",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Products_ProductId",
                table: "InventoryMovements",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Users_UserId",
                table: "InventoryMovements",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Companies_CompanyId",
                table: "Products",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Companies_CompanyId",
                table: "ProductStocks",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Establishments_EstablishmentId",
                table: "ProductStocks",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Products_ProductId",
                table: "ProductStocks",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Companies_CompanyId",
                table: "Sales",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Customers_CustomerId",
                table: "Sales",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_EmissionPoints_EmissionPointId",
                table: "Sales",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Establishments_EstablishmentId",
                table: "Sales",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Users_UserId",
                table: "Sales",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Companies_CompanyId",
                table: "Suppliers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_EmissionPoints_EmissionPointId",
                table: "Users",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Establishments_EstablishmentId",
                table: "Users",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Companies_CompanyId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanySriSettings_Companies_CompanyId",
                table: "CompanySriSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanySriSettings_Users_LastUpdatedByUserId",
                table: "CompanySriSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Companies_CompanyId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_Companies_CompanyId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_DocumentSequences_DocumentSequenceId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_EmissionPoints_EmissionPointId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_Establishments_EstablishmentId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequenceAudits_Users_UserId",
                table: "DocumentSequenceAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequences_Companies_CompanyId",
                table: "DocumentSequences");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequences_EmissionPoints_EmissionPointId",
                table: "DocumentSequences");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSequences_Establishments_EstablishmentId",
                table: "DocumentSequences");

            migrationBuilder.DropForeignKey(
                name: "FK_EmissionPoints_Establishments_EstablishmentId",
                table: "EmissionPoints");

            migrationBuilder.DropForeignKey(
                name: "FK_Establishments_Companies_CompanyId",
                table: "Establishments");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Companies_CompanyId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Establishments_EstablishmentId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Products_ProductId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Users_UserId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Companies_CompanyId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Companies_CompanyId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Establishments_EstablishmentId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Products_ProductId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Companies_CompanyId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Customers_CustomerId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_EmissionPoints_EmissionPointId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Establishments_EstablishmentId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Users_UserId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Companies_CompanyId",
                table: "Suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_EmissionPoints_EmissionPointId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Establishments_EstablishmentId",
                table: "Users");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Companies_CompanyId",
                table: "Categories",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySriSettings_Companies_CompanyId",
                table: "CompanySriSettings",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySriSettings_Users_LastUpdatedByUserId",
                table: "CompanySriSettings",
                column: "LastUpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Companies_CompanyId",
                table: "Customers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_Companies_CompanyId",
                table: "DocumentSequenceAudits",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_DocumentSequences_DocumentSequenceId",
                table: "DocumentSequenceAudits",
                column: "DocumentSequenceId",
                principalTable: "DocumentSequences",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_EmissionPoints_EmissionPointId",
                table: "DocumentSequenceAudits",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_Establishments_EstablishmentId",
                table: "DocumentSequenceAudits",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequenceAudits_Users_UserId",
                table: "DocumentSequenceAudits",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequences_Companies_CompanyId",
                table: "DocumentSequences",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequences_EmissionPoints_EmissionPointId",
                table: "DocumentSequences",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSequences_Establishments_EstablishmentId",
                table: "DocumentSequences",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmissionPoints_Establishments_EstablishmentId",
                table: "EmissionPoints",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Establishments_Companies_CompanyId",
                table: "Establishments",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Companies_CompanyId",
                table: "InventoryMovements",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Establishments_EstablishmentId",
                table: "InventoryMovements",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Products_ProductId",
                table: "InventoryMovements",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Users_UserId",
                table: "InventoryMovements",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Companies_CompanyId",
                table: "Products",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Companies_CompanyId",
                table: "ProductStocks",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Establishments_EstablishmentId",
                table: "ProductStocks",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Products_ProductId",
                table: "ProductStocks",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Companies_CompanyId",
                table: "Sales",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Customers_CustomerId",
                table: "Sales",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_EmissionPoints_EmissionPointId",
                table: "Sales",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Establishments_EstablishmentId",
                table: "Sales",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Users_UserId",
                table: "Sales",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Companies_CompanyId",
                table: "Suppliers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_EmissionPoints_EmissionPointId",
                table: "Users",
                column: "EmissionPointId",
                principalTable: "EmissionPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Establishments_EstablishmentId",
                table: "Users",
                column: "EstablishmentId",
                principalTable: "Establishments",
                principalColumn: "Id");
        }
    }
}
