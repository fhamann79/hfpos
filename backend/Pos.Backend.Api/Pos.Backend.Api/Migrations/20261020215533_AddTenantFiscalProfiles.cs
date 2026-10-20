using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantFiscalProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiscalFingerprint",
                table: "Sales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalIssuerSnapshotJson",
                table: "Sales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FiscalProfileId",
                table: "Sales",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalDecisionJson",
                table: "SaleItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FiscalRevision",
                table: "Products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FiscalIssuerSnapshotJson",
                table: "CreditNotes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FiscalProfileId",
                table: "CreditNotes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalDecisionJson",
                table: "CreditNoteItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Users_CompanyId_Id",
                table: "Users",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Products_CompanyId_Id",
                table: "Products",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "CompanyFiscalProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Regime = table.Column<int>(type: "integer", nullable: false),
                    TaxpayerKind = table.Column<int>(type: "integer", nullable: false),
                    IsAccountingRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SpecialTaxpayerResolution = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    WithholdingAgentResolution = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DeclaredByUserId = table.Column<int>(type: "integer", nullable: false),
                    DeclaredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    RimpeEligibilityConfirmed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyFiscalProfiles", x => x.Id);
                    table.UniqueConstraint("AK_CompanyFiscalProfiles_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CompanyFiscalProfiles_Declaration", "\"Revision\" > 0 AND \"Confirmed\" AND \"Regime\" BETWEEN 0 AND 2 AND \"TaxpayerKind\" BETWEEN 0 AND 1 AND (\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" > \"EffectiveFrom\")");
                    table.ForeignKey(
                        name: "FK_CompanyFiscalProfiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyFiscalProfiles_Users_CompanyId_DeclaredByUserId",
                        columns: x => new { x.CompanyId, x.DeclaredByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductFiscalClassifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ProductFiscalRevision = table.Column<long>(type: "bigint", nullable: false),
                    DeclaredVatCategory = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Activity = table.Column<int>(type: "integer", nullable: false),
                    TaxBasis = table.Column<int>(type: "integer", nullable: false),
                    LegalReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SriMaterialCode = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    NaturalAgriculturalFoodConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    FoodProcessing = table.Column<int>(type: "integer", nullable: true),
                    ConstructionMaterialConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    DeclaredByUserId = table.Column<int>(type: "integer", nullable: false),
                    DeclaredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFiscalClassifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductFiscalClassifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductFiscalClassifications_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductFiscalClassifications_Users_CompanyId_DeclaredByUser~",
                        columns: x => new { x.CompanyId, x.DeclaredByUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CompanyId_FiscalProfileId",
                table: "Sales",
                columns: new[] { "CompanyId", "FiscalProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CompanyId_FiscalProfileId",
                table: "CreditNotes",
                columns: new[] { "CompanyId", "FiscalProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyFiscalProfiles_CompanyId_DeclaredByUserId",
                table: "CompanyFiscalProfiles",
                columns: new[] { "CompanyId", "DeclaredByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyFiscalProfiles_CompanyId_EffectiveFrom_Revision",
                table: "CompanyFiscalProfiles",
                columns: new[] { "CompanyId", "EffectiveFrom", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyFiscalProfiles_CompanyId_Revision",
                table: "CompanyFiscalProfiles",
                columns: new[] { "CompanyId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductFiscalClassifications_CompanyId_DeclaredByUserId",
                table: "ProductFiscalClassifications",
                columns: new[] { "CompanyId", "DeclaredByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductFiscalClassifications_CompanyId_ProductId_Revision",
                table: "ProductFiscalClassifications",
                columns: new[] { "CompanyId", "ProductId", "Revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_CompanyFiscalProfiles_CompanyId_FiscalProfileId",
                table: "CreditNotes",
                columns: new[] { "CompanyId", "FiscalProfileId" },
                principalTable: "CompanyFiscalProfiles",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_CompanyFiscalProfiles_CompanyId_FiscalProfileId",
                table: "Sales",
                columns: new[] { "CompanyId", "FiscalProfileId" },
                principalTable: "CompanyFiscalProfiles",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_CompanyFiscalProfiles_CompanyId_FiscalProfileId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_CompanyFiscalProfiles_CompanyId_FiscalProfileId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "CompanyFiscalProfiles");

            migrationBuilder.DropTable(
                name: "ProductFiscalClassifications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Users_CompanyId_Id",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CompanyId_FiscalProfileId",
                table: "Sales");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Products_CompanyId_Id",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_CompanyId_FiscalProfileId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "FiscalFingerprint",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "FiscalIssuerSnapshotJson",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "FiscalProfileId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "FiscalDecisionJson",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "FiscalRevision",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "FiscalIssuerSnapshotJson",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "FiscalProfileId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "FiscalDecisionJson",
                table: "CreditNoteItems");
        }
    }
}
