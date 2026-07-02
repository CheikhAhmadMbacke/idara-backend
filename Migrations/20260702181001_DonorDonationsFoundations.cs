using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class DonorDonationsFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DonationAmountFcfa",
                table: "Withdrawals",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Withdrawals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "WalletTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DonorType",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DonationBalanceFcfa",
                table: "SchoolWallets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "DonorId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_DonorId_InitiatedAt",
                table: "Payments",
                columns: new[] { "DonorId", "InitiatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_DonorId",
                table: "Payments",
                column: "DonorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ----- Backfill Payment.Purpose -----
            // Aucun don n'existe encore. Les paiements sans Guardian (StudentId +
            // GuardianId null) sont donc des topups wallet école (§105) → Purpose=1
            // (WalletTopup). Les paiements parents (GuardianId non-null) restent à
            // 0 (SchoolFee, valeur par défaut de la colonne). Idempotent.
            migrationBuilder.Sql(
                "UPDATE \"Payments\" SET \"Purpose\" = 1 WHERE \"GuardianId\" IS NULL;");

            // ----- Backfill WalletTransaction.Source (badge historique) -----
            // Dérivé du RelatedEntity / Type existant, pour que l'historique wallet
            // n'affiche pas tout en « Paiement ». Aucun don historique (Source=1)
            // à poser. Valeurs : WalletRelatedEntity 1=Withdrawal 2=Subscription
            // 3=Topup ; WalletTransactionType 4=Adjustment. WalletSource 2=Topup
            // 3=Subscription 4=Withdrawal 5=Adjustment. Les crédits de paiement
            // (RelatedEntity=0) restent Source=0 (Payment).
            migrationBuilder.Sql(
                "UPDATE \"WalletTransactions\" SET \"Source\" = 4 WHERE \"RelatedEntity\" = 1;");
            migrationBuilder.Sql(
                "UPDATE \"WalletTransactions\" SET \"Source\" = 3 WHERE \"RelatedEntity\" = 2;");
            migrationBuilder.Sql(
                "UPDATE \"WalletTransactions\" SET \"Source\" = 2 WHERE \"RelatedEntity\" = 3;");
            migrationBuilder.Sql(
                "UPDATE \"WalletTransactions\" SET \"Source\" = 5 WHERE \"Type\" = 4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_DonorId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_DonorId_InitiatedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DonationAmountFcfa",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "DonorType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DonationBalanceFcfa",
                table: "SchoolWallets");

            migrationBuilder.DropColumn(
                name: "DonorId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Payments");
        }
    }
}
