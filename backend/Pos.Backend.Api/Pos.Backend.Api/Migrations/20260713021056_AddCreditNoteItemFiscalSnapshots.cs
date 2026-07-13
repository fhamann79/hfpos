using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Backend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteItemFiscalSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductAuxiliaryCodeSnapshot",
                table: "CreditNoteItems",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductMainCodeSnapshot",
                table: "CreditNoteItems",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "CreditNoteItems",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.Sql("""
                WITH normalized_products AS (
                    SELECT
                        p."Id",
                        NULLIF(
                            regexp_replace(btrim(COALESCE(p."Name", '')), '[[:space:]]+', ' ', 'g'),
                            '') AS normalized_name,
                        NULLIF(
                            left(
                                regexp_replace(btrim(COALESCE(p."InternalCode", '')), '[[:space:]]+', ' ', 'g'),
                                25),
                            '') AS normalized_internal_code,
                        NULLIF(
                            left(
                                regexp_replace(btrim(COALESCE(p."Barcode", '')), '[[:space:]]+', ' ', 'g'),
                                25),
                            '') AS normalized_barcode
                    FROM "Products" p
                )
                UPDATE "CreditNoteItems" cni
                SET
                    "ProductNameSnapshot" = left(
                        COALESCE(np.normalized_name, 'Producto ' || cni."ProductId"::text),
                        300),
                    "ProductMainCodeSnapshot" = left(
                        COALESCE(
                            np.normalized_internal_code,
                            np.normalized_barcode,
                            cni."ProductId"::text),
                        25),
                    "ProductAuxiliaryCodeSnapshot" = CASE
                        WHEN np.normalized_internal_code IS NOT NULL
                            AND np.normalized_barcode IS NOT NULL
                            AND np.normalized_barcode <> np.normalized_internal_code
                        THEN left(np.normalized_barcode, 25)
                        ELSE NULL
                    END
                FROM normalized_products np
                WHERE np."Id" = cni."ProductId";

                UPDATE "CreditNoteItems"
                SET
                    "ProductNameSnapshot" = COALESCE(
                        NULLIF("ProductNameSnapshot", ''),
                        left('Producto ' || "ProductId"::text, 300)),
                    "ProductMainCodeSnapshot" = COALESCE(
                        NULLIF("ProductMainCodeSnapshot", ''),
                        left("ProductId"::text, 25));
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ProductMainCodeSnapshot",
                table: "CreditNoteItems",
                type: "character varying(25)",
                maxLength: 25,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(25)",
                oldMaxLength: 25,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProductNameSnapshot",
                table: "CreditNoteItems",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductAuxiliaryCodeSnapshot",
                table: "CreditNoteItems");

            migrationBuilder.DropColumn(
                name: "ProductMainCodeSnapshot",
                table: "CreditNoteItems");

            migrationBuilder.DropColumn(
                name: "ProductNameSnapshot",
                table: "CreditNoteItems");
        }
    }
}
