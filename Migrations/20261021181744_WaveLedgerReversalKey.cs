using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class WaveLedgerReversalKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WaveLedgerEntries_TransactionId",
                table: "WaveLedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WaveLedgerEntries_TransactionId_TransactionType_IsReversal",
                table: "WaveLedgerEntries",
                columns: new[] { "TransactionId", "TransactionType", "IsReversal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WaveLedgerEntries_TransactionId_TransactionType_IsReversal",
                table: "WaveLedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WaveLedgerEntries_TransactionId",
                table: "WaveLedgerEntries",
                column: "TransactionId",
                unique: true);
        }
    }
}
