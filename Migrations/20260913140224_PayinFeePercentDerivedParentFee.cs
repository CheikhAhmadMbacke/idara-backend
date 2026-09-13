using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// La majoration au payeur cesse d'être un chiffre SAISI et devient une
    /// valeur DÉDUITE. La colonne <c>ParentFeePercent</c> laisse la place à
    /// <c>PayinFeePercent</c> — le taux que SenePay et l'opérateur prélèvent
    /// réellement à l'encaissement.
    ///
    /// <para>🔴 <b>ATTENTION — le piège du défaut, sixième forme.</b> EF a
    /// d'abord généré ceci, et rien d'autre :</para>
    /// <code>migrationBuilder.RenameColumn("ParentFeePercent", "PlatformSettings", "PayinFeePercent");</code>
    ///
    /// <para>Un renommage <b>conserve la valeur</b>. La ligne singleton porte
    /// <c>7.14</c> (l'ancienne majoration, saisie le 2026-08-18) : elle serait
    /// devenue un taux d'encaissement de 7,14 %, et la majoration déduite
    /// <c>(1 + 0,0177) / (1 − 0,0714)</c> serait montée à <b>9,6 %</b>. On
    /// aurait réclamé deux points de trop à chaque famille, sans la moindre
    /// erreur nulle part.</para>
    ///
    /// <para>C'est la même racine que §193, §202, §207, §232 et §254 — la ligne
    /// de réglages EXISTE, donc aucune valeur par défaut C# ne l'atteint — mais
    /// en pire : ici la colonne n'hérite pas de zéro, elle hérite d'un chiffre
    /// <b>vraisemblable</b> dont le SENS a changé. Un zéro se remarque ; 7,14 à
    /// la place de 5,37, non.</para>
    ///
    /// <para>D'où l'<c>UPDATE</c> explicite ci-dessous. Il ne porte pas de
    /// <c>WHERE</c> : <c>PlatformSettings</c> est un singleton (PK figée à 1) et
    /// la valeur d'avant n'a, par construction, aucun rapport avec celle
    /// d'après. Un <c>WHERE … = 0</c> comme dans les migrations précédentes ne
    /// protégerait de rien ici, puisque la valeur héritée n'est justement pas
    /// nulle.</para>
    /// </summary>
    public partial class PayinFeePercentDerivedParentFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ParentFeePercent",
                table: "PlatformSettings",
                newName: "PayinFeePercent");

            // 🔴 Sans cette ligne, la colonne garde 7.14 — voir le résumé.
            //
            // 5.37 = taux d'encaissement mesuré en production le 2026-09-12 sur
            // 199 paiements (3,6 % SenePay + 1,77 % opérateur). La valeur est
            // écrite EN CLAIR, pas interpolée depuis une constante : c'est elle
            // que `check-fee-neutrality.js` compare au défaut C#, et un contrôle
            // qui doit deviner une interpolation est un contrôle qui lâchera.
            migrationBuilder.Sql(
                "UPDATE \"PlatformSettings\" SET \"PayinFeePercent\" = 5.37;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PayinFeePercent",
                table: "PlatformSettings",
                newName: "ParentFeePercent");

            // Symétrie honnête : on ne peut pas « restituer » la majoration
            // d'avant, elle n'est plus stockée nulle part. On repose la valeur
            // qui était en production (7,14 %, sous-calibrée de 0,45 point),
            // pour qu'un retour en arrière retrouve l'état exact d'avant plutôt
            // qu'un taux d'encaissement pris pour une majoration.
            migrationBuilder.Sql(
                "UPDATE \"PlatformSettings\" SET \"ParentFeePercent\" = 7.14;");
        }
    }
}
