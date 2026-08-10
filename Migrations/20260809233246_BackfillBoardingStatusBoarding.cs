using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// Reprise ONE-SHOT du régime d'hébergement (2026-08-09).
    ///
    /// Contexte, à conserver : au moment où cette migration est écrite, les
    /// seuls élèves réels en production sont ceux d'un unique daara, et ils
    /// sont TOUS internes (confirmé par l'utilisateur). Les autres écoles de la
    /// base sont des écoles de test ou la démo Play Store. On les classe donc
    /// toutes en internat plutôt que de laisser un champ vide que personne ne
    /// remplirait, ce qui rendrait les onglets par régime inutilisables.
    ///
    /// ⚠️ AUCUN montant ne change le jour où cette migration s'applique :
    /// aucune école n'a encore de tarif par régime configuré (les colonnes
    /// viennent d'être créées, elles sont toutes nulles). Le premier tarif
    /// internat saisi s'appliquera en revanche à tous ces élèves — c'est bien
    /// l'intention, mais c'est à savoir.
    ///
    /// Écrite en SQL dans le Up() et non dans le code applicatif, comme toute
    /// conversion de données non rejouable (cf. CLAUDE.md §74). Le WHERE la
    /// rend inoffensive si elle était rejouée : elle ne touche QUE les lignes
    /// encore vides, jamais un régime déjà choisi par une école.
    /// </summary>
    public partial class BackfillBoardingStatusBoarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1 = BoardingStatus.Boarding (interne).
            migrationBuilder.Sql(
                "UPDATE \"Students\" SET \"BoardingStatus\" = 1 WHERE \"BoardingStatus\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volontairement vide : on ne peut pas distinguer un régime posé par
            // cette reprise d'un régime choisi ensuite par une école. Annuler
            // en masse effacerait le travail de saisie des daara.
        }
    }
}
