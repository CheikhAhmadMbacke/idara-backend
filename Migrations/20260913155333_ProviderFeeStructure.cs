using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// Les frais du prestataire cessent d'être DEUX POURCENTAGES et deviennent
    /// les quatre paramètres d'une règle.
    ///
    /// <para>🔴 <b>Pourquoi : les frais ne sont pas un pourcentage.</b> La règle
    /// réelle, retrouvée sur les 197 paiements réglés en production
    /// (<b>197/197 exacts</b>) :</para>
    ///
    /// <code>
    /// encaissement = round(C × 3,6 %) + ceil( ceil(C × 1,5 %) × 1,18 )
    /// décaissement =                    ceil( ceil(T × 1,5 %) × 1,18 )
    /// </code>
    ///
    /// <para>Le « 1,77 % » qu'on lisait partout est 1,5 % HT + 18 % de TVA,
    /// chacun arrondi au franc. Le « 5,40 % » d'encaissement est une moyenne
    /// vraie pour <b>aucun</b> montant : mesurée, elle va de 5,37 % à 6,05 %
    /// selon la taille du paiement. Appliquer un taux moyen laissait
    /// <b>3 542 montants en déficit</b> entre 200 et 100 000 FCFA, et faisait
    /// payer jusqu'à 31 F de trop ailleurs. En résolvant au franc :
    /// <b>0 déficit, 0 franc de trop</b>.</para>
    ///
    /// <para>🔑 <b>AUCUNE VALEUR PAR DÉFAUT N'EXISTE DANS LE CODE.</b> Les quatre
    /// colonnes sont nullables et les propriétés C# n'ont pas d'initialisateur :
    /// une base neuve démarre donc SANS taux, et tout encaissement « frais au
    /// payeur » y est refusé avec un message explicite, jusqu'à saisie. C'est
    /// voulu — une valeur de repli « raisonnable » est exactement ce qui a laissé
    /// une majoration fausse tourner quatre mois sans que rien ne le signale.</para>
    ///
    /// <para>⚠️ <b>L'UPDATE ci-dessous n'est PAS un défaut, c'est une reprise
    /// d'état.</b> Ces quatre nombres ne sont pas choisis : ils sont
    /// <b>mesurés</b> sur la production, et ce sont eux qui expliquent
    /// exactement les 197 paiements observés. Les omettre couperait les
    /// encaissements de la plateforme en service entre le déploiement et la
    /// saisie — une interruption que personne n'a demandée. Ils sont modifiables
    /// en dix secondes depuis SuperAdmin → Réglages plateforme, et c'est la base
    /// qui fait foi ensuite, jamais le code.</para>
    /// </summary>
    public partial class ProviderFeeStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayinFeePercent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PayoutFeePercent",
                table: "PlatformSettings");

            migrationBuilder.AddColumn<double>(
                name: "PayinProviderFeePercent",
                table: "PlatformSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PayinOperatorFeePercentHt",
                table: "PlatformSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PayoutOperatorFeePercentHt",
                table: "PlatformSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FeeVatPercent",
                table: "PlatformSettings",
                type: "double precision",
                nullable: true);

            // Reprise d'état : les taux MESURÉS sur la production (voir le résumé).
            // La ligne de réglages existe depuis juin ; sans cette écriture elle
            // resterait à NULL et la plateforme cesserait d'encaisser.
            migrationBuilder.Sql(@"
                UPDATE ""PlatformSettings""
                   SET ""PayinProviderFeePercent""    = 3.6,
                       ""PayinOperatorFeePercentHt""  = 1.5,
                       ""PayoutOperatorFeePercentHt"" = 1.5,
                       ""FeeVatPercent""              = 18.0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeeVatPercent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PayinOperatorFeePercentHt",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PayinProviderFeePercent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PayoutOperatorFeePercentHt",
                table: "PlatformSettings");

            migrationBuilder.AddColumn<double>(
                name: "PayinFeePercent",
                table: "PlatformSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PayoutFeePercent",
                table: "PlatformSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // Un retour en arrière doit retrouver l'état d'avant, pas des zéros
            // qui feraient tourner l'ancienne formule sur du vide.
            migrationBuilder.Sql(@"
                UPDATE ""PlatformSettings""
                   SET ""PayinFeePercent""  = 5.40,
                       ""PayoutFeePercent"" = 1.77;");
        }
    }
}
