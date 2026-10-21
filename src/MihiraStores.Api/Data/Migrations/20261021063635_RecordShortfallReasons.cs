using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MihiraStores.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecordShortfallReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptNote",
                table: "Transfers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortfallReason",
                table: "TransferLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferLines_ShortfallReason",
                table: "TransferLines",
                sql: "([QuantityReceived] IS NULL AND [ShortfallReason] IS NULL) OR ([QuantityReceived] = [Quantity] AND [ShortfallReason] IS NULL) OR ([QuantityReceived] < [Quantity] AND [ShortfallReason] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferLines_ShortfallReason",
                table: "TransferLines");

            migrationBuilder.DropColumn(
                name: "ReceiptNote",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ShortfallReason",
                table: "TransferLines");
        }
    }
}
