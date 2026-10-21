using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MihiraStores.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BindIdempotencyKeyToRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyRequestHash",
                table: "Transfers",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdempotencyRequestHash",
                table: "Transfers");
        }
    }
}
