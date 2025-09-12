using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting_System.Migrations
{
    /// <inheritdoc />
    public partial class ChangeReferenceColumnLengthInCheckVoucherHeaderModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "reference",
                table: "check_voucher_headers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(13)",
                oldMaxLength: 13,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "reference",
                table: "check_voucher_headers",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
