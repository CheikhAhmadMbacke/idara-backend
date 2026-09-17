using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class WaveDirectProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SenePayDisbursementId",
                table: "Withdrawals",
                newName: "ProviderDisbursementId");

            migrationBuilder.RenameIndex(
                name: "IX_Withdrawals_SenePayDisbursementId",
                table: "Withdrawals",
                newName: "IX_Withdrawals_ProviderDisbursementId");

            migrationBuilder.RenameColumn(
                name: "SenePayReference",
                table: "PlatformOutflows",
                newName: "ProviderReference");

            migrationBuilder.RenameIndex(
                name: "IX_PlatformOutflows_SenePayReference",
                table: "PlatformOutflows",
                newName: "IX_PlatformOutflows_ProviderReference");

            migrationBuilder.RenameColumn(
                name: "SenePayTransactionId",
                table: "Payments",
                newName: "ProviderTransactionId");

            migrationBuilder.RenameColumn(
                name: "SenePayInternalId",
                table: "Payments",
                newName: "ProviderInternalId");

            migrationBuilder.RenameIndex(
                name: "IX_Payments_SenePayTransactionId",
                table: "Payments",
                newName: "IX_Payments_ProviderTransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_Payments_SenePayInternalId",
                table: "Payments",
                newName: "IX_Payments_ProviderInternalId");

            // defaultValue = "SenePay", PAS le defaut C# ("Wave") ni la chaine
            // vide qu'EF a proposee. C'est la SEPTIEME occurrence du meme piege
            // (§193, §202, §207, §232, §254) : la valeur posee ici est celle que
            // recoivent les 203 paiements et 102 retraits DEJA en base, et
            // l'histoire leur appartient - ils ont ete traites par le prestataire
            // precedent. Avec "" ou "Wave", le travail de verification irait
            // demander a Wave des nouvelles d'operations qu'il n'a jamais vues,
            // et la reconciliation les compterait dans la mauvaise reserve.
            // valeur-conservee-voulue: l'historique reste au prestataire d'origine
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Withdrawals",
                type: "text",
                nullable: false,
                defaultValue: "SenePay");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Payments",
                type: "text",
                nullable: false,
                defaultValue: "SenePay");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Payments");

            migrationBuilder.RenameColumn(
                name: "ProviderDisbursementId",
                table: "Withdrawals",
                newName: "SenePayDisbursementId");

            migrationBuilder.RenameIndex(
                name: "IX_Withdrawals_ProviderDisbursementId",
                table: "Withdrawals",
                newName: "IX_Withdrawals_SenePayDisbursementId");

            migrationBuilder.RenameColumn(
                name: "ProviderReference",
                table: "PlatformOutflows",
                newName: "SenePayReference");

            migrationBuilder.RenameIndex(
                name: "IX_PlatformOutflows_ProviderReference",
                table: "PlatformOutflows",
                newName: "IX_PlatformOutflows_SenePayReference");

            migrationBuilder.RenameColumn(
                name: "ProviderTransactionId",
                table: "Payments",
                newName: "SenePayTransactionId");

            migrationBuilder.RenameColumn(
                name: "ProviderInternalId",
                table: "Payments",
                newName: "SenePayInternalId");

            migrationBuilder.RenameIndex(
                name: "IX_Payments_ProviderTransactionId",
                table: "Payments",
                newName: "IX_Payments_SenePayTransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_Payments_ProviderInternalId",
                table: "Payments",
                newName: "IX_Payments_SenePayInternalId");
        }
    }
}
