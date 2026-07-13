using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteCancellationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "CreditNotes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "CreditNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CancelledByUserId",
                table: "CreditNotes",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_Users_CancelledByUserId",
                table: "CreditNotes",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_Users_CancelledByUserId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_CancelledByUserId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "CreditNotes");
        }
    }
}
