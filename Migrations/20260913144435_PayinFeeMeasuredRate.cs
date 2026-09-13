using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// Recale le taux d'encaissement sur ce que la production prélève
    /// <b>vraiment</b> : 5,40 % au lieu de 5,37 %.
    ///
    /// <para>🔴 <b>Le taux contractuel est un PLANCHER, pas une moyenne.</b>
    /// 3,6 % (SenePay) + 1,77 % (opérateur) = 5,37 — et c'est bien le
    /// <b>minimum</b> observé sur les 197 paiements réglés. Mais SenePay
    /// arrondit ses frais au franc supérieur sur <i>chaque</i> transaction, et
    /// ce supplément pèse d'autant plus que le montant est petit :</para>
    ///
    /// <list type="table">
    ///   <item><description>sous 1 000 F → <b>5,749 %</b></description></item>
    ///   <item><description>de 1 000 à 5 000 F → <b>5,532 %</b></description></item>
    ///   <item><description>de 5 000 à 20 000 F → <b>5,380 %</b></description></item>
    ///   <item><description>au-delà de 20 000 F → <b>5,373 %</b></description></item>
    /// </list>
    ///
    /// <para>Moyenne pondérée par les montants : <b>5,379 %</b>. Saisir 5,37
    /// laissait donc ~2 F de déficit par transaction — 29 fois moins que les
    /// 7,14 % d'avant, mais toujours du déficit, alors que la demande était
    /// explicitement « sans que la plateforme ne perde ».</para>
    ///
    /// <para>5,40 place le taux légèrement <b>au-dessus</b> du mesuré : le
    /// coussin absorbe les arrondis sur l'essentiel du volume (188 paiements sur
    /// 197 dépassent 5 000 F). Pour une famille, cela représente <b>+3 F</b> sur
    /// une mensualité de 10 000.</para>
    ///
    /// <para>⚠️ Les très petits paiements (moins de 1 000 F, 3 cas sur 197)
    /// restent structurellement déficitaires de quelques francs : l'arrondi au
    /// franc du prestataire y pèse plus que n'importe quelle majoration
    /// raisonnable. C'est un fait du moyen de paiement, pas un défaut de calcul —
    /// et l'écran de contrôle le montre plutôt que de le masquer.</para>
    ///
    /// <para>⚠️ <b>EF a généré cette migration VIDE</b> (§61), et c'est normal :
    /// aucune colonne ne change, seule la VALEUR de la ligne singleton bouge —
    /// ce qu'aucune comparaison de modèle ne peut voir. Une migration vide
    /// laissée telle quelle serait passée sans bruit.</para>
    ///
    /// <para>Cette migration n'existe que pour que le <b>code</b> et la
    /// <b>base</b> ne divergent jamais (contrôlé par
    /// <c>check-fee-neutrality.js</c>). Le taux reste réglable sans
    /// redéploiement depuis SuperAdmin — c'est tout l'objet du §255.</para>
    /// </summary>
    public partial class PayinFeeMeasuredRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"PlatformSettings\" SET \"PayinFeePercent\" = 5.40;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"PlatformSettings\" SET \"PayinFeePercent\" = 5.37;");
        }
    }
}
