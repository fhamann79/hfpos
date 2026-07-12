using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    EmissionPointId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OriginalSaleId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    BuyerNameSnapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BuyerIdentificationTypeSnapshot = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    BuyerIdentificationSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BuyerAddressSnapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BuyerEmailSnapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    OriginalSaleNumberSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OriginalSaleAccessKeySnapshot = table.Column<string>(type: "character varying(49)", maxLength: 49, nullable: true),
                    OriginalSaleAuthorizationNumberSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OriginalSaleAuthorizedAtSnapshot = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OriginalSaleDocumentIssuedAtSnapshot = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocumentStatus = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EstablishmentCodeSnapshot = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    EmissionPointCodeSnapshot = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Sequential = table.Column<int>(type: "integer", nullable: true),
                    DocumentIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AccessKey = table.Column<string>(type: "character varying(49)", maxLength: 49, nullable: true),
                    AuthorizationNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SriEnvironment = table.Column<int>(type: "integer", nullable: true),
                    SriEmissionType = table.Column<int>(type: "integer", nullable: true),
                    SriNumericCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SriSubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SriReceptionStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SriAuthorizationStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SriLastSubmissionError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SriLastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GrossSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat15Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat5Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat0Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatExemptSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatNotSubjectSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneIdSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "America/Guayaquil"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_EmissionPoints_EmissionPointId",
                        column: x => x.EmissionPointId,
                        principalTable: "EmissionPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Sales_OriginalSaleId",
                        column: x => x.OriginalSaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNoteItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreditNoteId = table.Column<int>(type: "integer", nullable: false),
                    SaleItemId = table.Column<int>(type: "integer", nullable: true),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    GrossSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatCategory = table.Column<int>(type: "integer", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxableSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNoteItems_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreditNoteItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteItems_SaleItems_SaleItemId",
                        column: x => x.SaleItemId,
                        principalTable: "SaleItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteItems_CreditNoteId",
                table: "CreditNoteItems",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteItems_ProductId",
                table: "CreditNoteItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteItems_SaleItemId",
                table: "CreditNoteItems",
                column: "SaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_BusinessDate",
                table: "CreditNotes",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CompanyId",
                table: "CreditNotes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CompanyId_EstablishmentId_EmissionPointId_Seque~",
                table: "CreditNotes",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "Sequential" },
                unique: true,
                filter: "\"Sequential\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CreatedAt",
                table: "CreditNotes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomerId",
                table: "CreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_DocumentStatus",
                table: "CreditNotes",
                column: "DocumentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_EmissionPointId",
                table: "CreditNotes",
                column: "EmissionPointId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_EstablishmentId",
                table: "CreditNotes",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_Number",
                table: "CreditNotes",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OriginalSaleId",
                table: "CreditNotes",
                column: "OriginalSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_UserId",
                table: "CreditNotes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditNoteItems");

            migrationBuilder.DropTable(
                name: "CreditNotes");
        }
    }
}
