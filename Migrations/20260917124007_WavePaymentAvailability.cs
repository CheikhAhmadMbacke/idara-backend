using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class WavePaymentAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayinDisabledReason",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            // ================================================================
            // defaut-zero-voulu: les guichets s'ouvrent FERMES, et c'est le but
            //
            // EF propose `false` parce que c'est le defaut d'un booleen ; ici,
            // pour une fois, c'est exactement ce qu'on veut - mais il faut que
            // ce soit ECRIT, pas subi (§193, §202, §207, §232, §254).
            //
            // Au premier demarrage qui suit la bascule vers Wave, personne n'a
            // encore prouve qu'un franc circule par la nouvelle chaine. Ouvrir
            // les guichets reviendrait a faire de la premiere vraie famille le
            // cobaye. On ferme donc, on eprouve avec de l'argent reel sur
            // l'ecole de demonstration - qui, elle, n'est jamais bloquee - puis
            // on rouvre d'un geste depuis le back-office.
            //
            // Le defaut C# reste `true` : une plateforme NEUVE doit encaisser.
            // ================================================================
            migrationBuilder.AddColumn<bool>(
                name: "PayinEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PayoutDisabledReason",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PayoutEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Motif affiche aux utilisateurs pendant la fermeture initiale. Un
            // ecran qui dit « indisponible » sans rien d'autre fait appeler
            // l'ecole ; celui-ci annonce une duree et une raison.
            migrationBuilder.Sql(@"
                UPDATE ""PlatformSettings""
                   SET ""PayinDisabledReason"" = 'Les paiements en ligne sont momentanement suspendus, le temps de finaliser le passage a notre nouveau prestataire. Reessayez dans quelques heures.',
                       ""PayoutDisabledReason"" = 'Les retraits sont momentanement suspendus, le temps de finaliser le passage a notre nouveau prestataire. Reessayez dans quelques heures.';");

            // ================================================================
            // Article 8.2 du contrat Wave : plus AUCUNE majoration au payeur.
            //
            // Les six ecoles en production encaissaient en mode « le parent
            // paie les frais » - 93 811 F preleves sur les familles en trente
            // jours. Facturer des frais a un payeur pour regler via Wave, c'est
            // la resiliation SANS PREAVIS. La donnee doit donc basculer EN MEME
            // TEMPS que le code : un deploiement ou le code est pret et la base
            // encore en mode « Parent » est precisement la fenetre dangereuse.
            // ================================================================
            migrationBuilder.Sql(@"
                UPDATE ""SchoolPaymentSettings""
                   SET ""FeesPayer"" = 1
                 WHERE ""FeesPayer"" <> 1;");

            migrationBuilder.Sql(@"
                UPDATE ""SchoolPaymentSettings""
                   SET ""DonationFeesPayer"" = 1
                 WHERE ""DonationFeesPayer"" <> 1;");

            // Les collectes de dons portent leur propre mode : meme regle.
            migrationBuilder.Sql(@"
                UPDATE ""DonationCampaigns""
                   SET ""FeesPayer"" = 1
                 WHERE ""FeesPayer"" <> 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayinDisabledReason",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PayinEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PayoutDisabledReason",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PayoutEnabled",
                table: "PlatformSettings");
        }
    }
}
