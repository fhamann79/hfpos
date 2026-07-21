using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteSriSubmissionAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SaleId",
                table: "SriSubmissionAttempts",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "CreditNoteId",
                table: "SriSubmissionAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SriSubmissionAttempts_CreditNoteId_CreatedAt",
                table: "SriSubmissionAttempts",
                columns: new[] { "CreditNoteId", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SriSubmissionAttempts_ExactlyOneDocument",
                table: "SriSubmissionAttempts",
                sql: "((\"SaleId\" IS NOT NULL AND \"CreditNoteId\" IS NULL) OR (\"SaleId\" IS NULL AND \"CreditNoteId\" IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_SriSubmissionAttempts_CreditNotes_CreditNoteId",
                table: "SriSubmissionAttempts",
                column: "CreditNoteId",
                principalTable: "CreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SriSubmissionAttempts_CreditNotes_CreditNoteId",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropIndex(
                name: "IX_SriSubmissionAttempts_CreditNoteId_CreatedAt",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SriSubmissionAttempts_ExactlyOneDocument",
                table: "SriSubmissionAttempts");

            migrationBuilder.DropColumn(
                name: "CreditNoteId",
                table: "SriSubmissionAttempts");

            migrationBuilder.AlterColumn<int>(
                name: "SaleId",
                table: "SriSubmissionAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
