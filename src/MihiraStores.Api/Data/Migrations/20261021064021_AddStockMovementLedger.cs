using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MihiraStores.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Change = table.Column<int>(type: "int", nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    TransferId = table.Column<int>(type: "int", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_BalanceAfter_NonNegative", "[BalanceAfter] >= 0");
                    table.CheckConstraint("CK_StockMovements_Change_NonZero", "[Change] <> 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Transfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "Transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BranchId_ProductId_Id",
                table: "StockMovements",
                columns: new[] { "BranchId", "ProductId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TransferId",
                table: "StockMovements",
                column: "TransferId");

            // Databases created before the ledger already hold stock. Record it as opening stock so that,
            // from here on, the ledger always sums to the stock level. (On a new database this inserts
            // nothing: the seeder runs after migrations and writes its own opening entries.)
            migrationBuilder.Sql("""
                INSERT INTO [StockMovements] ([BranchId], [ProductId], [Kind], [Change], [BalanceAfter], [TransferId], [OccurredAt])
                SELECT [BranchId], [ProductId], N'Opening', [Quantity], [Quantity], NULL, SYSDATETIMEOFFSET()
                FROM [StockLevels]
                WHERE [Quantity] <> 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovements");
        }
    }
}
