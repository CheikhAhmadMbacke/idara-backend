using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// 📅 Sépare le jour d'OUVERTURE du paiement du jour LIMITE.
    ///
    /// <para>Jusqu'ici, un seul jour faisait les deux : la mensualité était émise
    /// le 5 <b>et</b> échue le 5. Dès le 6 au matin, toute famille qui n'avait pas
    /// payé passait « en retard » et recevait un SMS de relance. Aucune fenêtre
    /// de paiement.</para>
    /// </summary>
    public partial class PaymentDeadlineDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 🔴 `defaultValue` CORRIGÉ À LA MAIN — EF avait généré 0.
            //
            // Un jour limite à 0 n'existe pas dans un mois : toutes les factures
            // seraient nées échues et auraient déclenché une vague de rappels de
            // retard dès le lendemain du déploiement. La question à se poser pour
            // toute colonne ajoutée est « quelle valeur doivent avoir les lignes
            // DÉJÀ en base ? » (§152/§158) — ici : une fenêtre de paiement réelle.
            migrationBuilder.AddColumn<int>(
                name: "PaymentDeadlineDay",
                table: "SchoolPaymentSettings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            // Puis on l'affine école par école : dix jours après SON ouverture
            // (décision produit 2026-08-23), borné au 28 comme les deux réglages.
            // Une école qui ouvre le 5 aura jusqu'au 15 ; une qui ouvre le 25,
            // jusqu'au 28.
            //
            // ⚠️ Cette reprise change un comportement sans que l'école l'ait
            // demandé — assumé : aucune n'a jamais CHOISI de relancer ses parents
            // dès le lendemain de l'émission, c'était un défaut, pas un réglage.
            migrationBuilder.Sql(
                "UPDATE \"SchoolPaymentSettings\" " +
                "SET \"PaymentDeadlineDay\" = LEAST(\"MonthlyDueDay\" + 10, 28);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentDeadlineDay",
                table: "SchoolPaymentSettings");
        }
    }
}
