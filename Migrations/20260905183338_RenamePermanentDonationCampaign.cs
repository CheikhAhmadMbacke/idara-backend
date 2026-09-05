using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class RenamePermanentDonationCampaign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Le lien permanent s'appelait « Dons au daara » chez toutes les écoles
            // déjà inscrites. Le mot excluait les écoles franco-arabes et tout
            // établissement qui ne se dit pas daara (§186) — alors que ce nom est la
            // PREMIÈRE chose qu'un donateur lit sur la page publique.
            //
            // ⚠️ On ne renomme QUE les collectes restées au nom d'origine. Une école
            // qui a choisi son propre intitulé garde le sien : une migration
            // n'écrase jamais une décision prise par l'utilisateur.
            migrationBuilder.Sql(
                "UPDATE \"DonationCampaigns\" " +
                "SET \"Name\" = 'Dons à l''établissement' " +
                "WHERE \"IsPermanent\" = TRUE AND \"Name\" = 'Dons au daara';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"DonationCampaigns\" " +
                "SET \"Name\" = 'Dons au daara' " +
                "WHERE \"IsPermanent\" = TRUE AND \"Name\" = 'Dons à l''établissement';");
        }
    }
}
