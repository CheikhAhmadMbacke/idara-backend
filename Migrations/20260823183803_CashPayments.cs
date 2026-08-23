using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class CashPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CollectedById",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentId",
                table: "CashLedgerEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CollectedById",
                table: "Payments",
                column: "CollectedById");

            migrationBuilder.CreateIndex(
                name: "IX_CashLedgerEntries_PaymentId",
                table: "CashLedgerEntries",
                column: "PaymentId",
                filter: "\"PaymentId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CashLedgerEntries_Payments_PaymentId",
                table: "CashLedgerEntries",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_CollectedById",
                table: "Payments",
                column: "CollectedById",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashLedgerEntries_Payments_PaymentId",
                table: "CashLedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_CollectedById",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CollectedById",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_CashLedgerEntries_PaymentId",
                table: "CashLedgerEntries");

            migrationBuilder.DropColumn(
                name: "CollectedById",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "CashLedgerEntries");
        }
    }
}
