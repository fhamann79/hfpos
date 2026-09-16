using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditNoteRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreditNoteId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EstablishmentId = table.Column<int>(type: "integer", nullable: false),
                    EmissionPointId = table.Column<int>(type: "integer", nullable: false),
                    RefundedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneIdSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CashSessionId = table.Column<int>(type: "integer", nullable: true),
                    CashMovementId = table.Column<int>(type: "integer", nullable: true),
                    Reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteRefunds", x => x.Id);
                    table.CheckConstraint("CK_CreditNoteRefunds_AmountPositive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CreditNoteRefunds_CashLinks", "((\"Method\" = 0 AND \"CashSessionId\" IS NOT NULL AND \"CashMovementId\" IS NOT NULL) OR (\"Method\" <> 0 AND \"CashSessionId\" IS NULL AND \"CashMovementId\" IS NULL))");
                    table.CheckConstraint("CK_CreditNoteRefunds_Method", "\"Method\" IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_CreditNoteRefunds_CashMovements_CashMovementId",
                        column: x => x.CashMovementId,
                        principalTable: "CashMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteRefunds_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteRefunds_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteRefunds_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteRefunds_EmissionPoints_EmissionPointId",
                        column: x => x.EmissionPointId,
                        principalTable: "EmissionPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteRefunds_Establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "Establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteRefunds_Users_RefundedByUserId",
                        column: x => x.RefundedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_CashMovementId",
                table: "CreditNoteRefunds",
                column: "CashMovementId",
                unique: true,
                filter: "\"CashMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_CashSessionId",
                table: "CreditNoteRefunds",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_CompanyId_EstablishmentId_BusinessDate",
                table: "CreditNoteRefunds",
                columns: new[] { "CompanyId", "EstablishmentId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_CreditNoteId",
                table: "CreditNoteRefunds",
                column: "CreditNoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_EmissionPointId",
                table: "CreditNoteRefunds",
                column: "EmissionPointId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_EstablishmentId",
                table: "CreditNoteRefunds",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteRefunds_RefundedByUserId",
                table: "CreditNoteRefunds",
                column: "RefundedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditNoteRefunds");
        }
    }
}
