using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    EmissionPointId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    GrossSalesAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VoidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedNetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DifferenceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReconciledByUserId = table.Column<int>(type: "integer", nullable: false),
                    ReconciledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimeZoneIdSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentSettlements", x => x.Id);
                    table.CheckConstraint("CK_PaymentSettlements_Difference", "\"DifferenceAmount\" = \"SettledAmount\" - \"ExpectedNetAmount\"");
                    table.CheckConstraint("CK_PaymentSettlements_ExpectedNet", "\"ExpectedNetAmount\" = \"GrossSalesAmount\" - \"VoidAmount\" - \"RefundAmount\"");
                    table.CheckConstraint("CK_PaymentSettlements_Method", "\"PaymentMethod\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_PaymentSettlements_NonnegativeComponents", "\"GrossSalesAmount\" >= 0 AND \"VoidAmount\" >= 0 AND \"RefundAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PaymentSettlements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentSettlements_EmissionPoints_EmissionPointId",
                        column: x => x.EmissionPointId,
                        principalTable: "EmissionPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentSettlements_Establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentSettlements_Users_ReconciledByUserId",
                        column: x => x.ReconciledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_BusinessDat~",
                table: "Sales",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "BusinessDate", "PaymentMethod" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_VoidBusines~",
                table: "Sales",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "VoidBusinessDate", "PaymentMethod" },
                filter: "\"VoidBusinessDate\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_CompanyId_EstablishmentId_EmissionPointId~",
                table: "CreditNoteRefunds",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "BusinessDate", "Method" });

            migrationBuilder.CreateIndex(
                name: "UX_PaymentSettlements_ContextDateMethod",
                table: "PaymentSettlements",
                columns: new[] { "CompanyId", "EstablishmentId", "EmissionPointId", "BusinessDate", "PaymentMethod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlements_CompanyId_RequestId",
                table: "PaymentSettlements",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlements_EmissionPointId",
                table: "PaymentSettlements",
                column: "EmissionPointId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlements_EstablishmentId",
                table: "PaymentSettlements",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlements_ReconciledByUserId",
                table: "PaymentSettlements",
                column: "ReconciledByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentSettlements");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_BusinessDat~",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CompanyId_EstablishmentId_EmissionPointId_VoidBusines~",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_CreditNoteRefunds_CompanyId_EstablishmentId_EmissionPointId~",
                table: "CreditNoteRefunds");
        }
    }
}
