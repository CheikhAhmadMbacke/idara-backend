using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// Enregistre ce qui a été RÉELLEMENT crédité au wallet de l'école, au lieu
    /// de le redéduire à chaque lecture.
    ///
    /// <para>🔑 <b>Pourquoi cette colonne existe.</b> La part de la plateforme
    /// est <c>NetCreditedFcfa − WalletCreditedFcfa</c>, et elle entre dans
    /// l'identité comptable <c>R = D + P</c> (§112). Tant que le crédit se
    /// déduisait d'une règle (« la cible en mode Parent, le net en mode
    /// School »), la règle suffisait. Elle ne suffit plus : en mode School, le
    /// crédit dépend désormais du <b>frais de retrait provisionné</b>, donc des
    /// taux du prestataire. Les redéduire plus tard reviendrait à recalculer le
    /// passé avec les taux d'aujourd'hui — les comptes de juin changeraient à
    /// chaque fois que SenePay change sa grille. <b>Un montant qui a été écrit
    /// se lit, il ne se déduit pas.</b></para>
    ///
    /// <para>🔴 <b>Le remplissage rétroactif est OBLIGATOIRE</b>, et c'est le
    /// piège habituel sous une forme nouvelle : la colonne naît à
    /// <c>0</c> pour les 199 paiements déjà réglés. Or <c>0</c> n'y est pas une
    /// valeur neutre — il signifierait « rien n'a été crédité à l'école », donc
    /// « tout est un gain plateforme ». P bondirait de plusieurs millions et
    /// l'identité <c>R = D + P</c> volerait en éclats, sans la moindre erreur
    /// nulle part. Le <c>UPDATE</c> ci-dessous réécrit donc l'historique avec la
    /// règle <b>qui était en vigueur au moment de chaque crédit</b> :</para>
    ///
    /// <list type="bullet">
    ///   <item><description>mode Parent avec une cible → la cible a été
    ///   créditée ;</description></item>
    ///   <item><description>sinon → le net a été crédité ;</description></item>
    ///   <item><description>sauf espèces (l'argent est allé en caisse, §182) et
    ///   achats de pages (aucun wallet crédité, §233) → <c>0</c>, qui est cette
    ///   fois la valeur juste.</description></item>
    /// </list>
    ///
    /// <para>Les paiements non réglés (Pending, Failed…) restent à 0 : rien n'a
    /// été crédité, et le règlement écrira la valeur le moment venu.</para>
    /// </summary>
    public partial class WalletCreditedOnPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Zéro est la bonne valeur de départ : un paiement non réglé n'a
            // rien crédité. Ceux qui l'ont été sont réécrits juste après.
            migrationBuilder.AddColumn<long>(
                name: "WalletCreditedFcfa",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Reconstitution de l'historique avec la règle de l'époque.
            // Status = 1 → Completed. Operator = 2 → Cash. Purpose = 3 → OcrPages.
            migrationBuilder.Sql(@"
                UPDATE ""Payments""
                   SET ""WalletCreditedFcfa"" =
                       CASE
                           WHEN ""FeesPayer"" = 0 AND ""TargetAmountFcfa"" > 0
                               THEN ""TargetAmountFcfa""
                           ELSE ""NetCreditedFcfa""
                       END
                 WHERE ""Status"" = 1
                   AND ""Operator"" <> 2
                   AND ""Purpose"" <> 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WalletCreditedFcfa",
                table: "Payments");
        }
    }
}
