using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class WaveIndexFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Withdrawals_ProviderDisbursementId",
                table: "Withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_PlatformOutflows_ProviderReference",
                table: "PlatformOutflows");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderInternalId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderTransactionId",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_ProviderDisbursementId",
                table: "Withdrawals",
                column: "ProviderDisbursementId",
                unique: true,
                filter: "\"ProviderDisbursementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOutflows_ProviderReference",
                table: "PlatformOutflows",
                column: "ProviderReference",
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderInternalId",
                table: "Payments",
                column: "ProviderInternalId",
                filter: "\"ProviderInternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderTransactionId",
                table: "Payments",
                column: "ProviderTransactionId",
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Withdrawals_ProviderDisbursementId",
                table: "Withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_PlatformOutflows_ProviderReference",
                table: "PlatformOutflows");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderInternalId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderTransactionId",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_ProviderDisbursementId",
                table: "Withdrawals",
                column: "ProviderDisbursementId",
                unique: true,
                filter: "\"SenePayDisbursementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOutflows_ProviderReference",
                table: "PlatformOutflows",
                column: "ProviderReference",
                unique: true,
                filter: "\"SenePayReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderInternalId",
                table: "Payments",
                column: "ProviderInternalId",
                filter: "\"SenePayInternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderTransactionId",
                table: "Payments",
                column: "ProviderTransactionId",
                unique: true,
                filter: "\"SenePayTransactionId\" IS NOT NULL");
        }
    }
}
