using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteEmailDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SaleId",
                table: "SaleInvoiceEmailDeliveries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "CreditNoteId",
                table: "SaleInvoiceEmailDeliveries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceEmailDeliveries_CreditNoteId_CreatedAt",
                table: "SaleInvoiceEmailDeliveries",
                columns: new[] { "CreditNoteId", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SaleInvoiceEmailDeliveries_ExactlyOneDocument",
                table: "SaleInvoiceEmailDeliveries",
                sql: "((\"SaleId\" IS NOT NULL AND \"CreditNoteId\" IS NULL) OR (\"SaleId\" IS NULL AND \"CreditNoteId\" IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleInvoiceEmailDeliveries_CreditNotes_CreditNoteId",
                table: "SaleInvoiceEmailDeliveries",
                column: "CreditNoteId",
                principalTable: "CreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleInvoiceEmailDeliveries_CreditNotes_CreditNoteId",
                table: "SaleInvoiceEmailDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoiceEmailDeliveries_CreditNoteId_CreatedAt",
                table: "SaleInvoiceEmailDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SaleInvoiceEmailDeliveries_ExactlyOneDocument",
                table: "SaleInvoiceEmailDeliveries");

            migrationBuilder.DropColumn(
                name: "CreditNoteId",
                table: "SaleInvoiceEmailDeliveries");

            migrationBuilder.AlterColumn<int>(
                name: "SaleId",
                table: "SaleInvoiceEmailDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
