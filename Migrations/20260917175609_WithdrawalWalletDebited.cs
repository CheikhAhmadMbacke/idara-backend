using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class WithdrawalWalletDebited : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WalletDebitedFcfa",
                table: "Withdrawals",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // 🔑 LE DÉFAUT À ZÉRO NE VAUT QUE POUR LES LIGNES À VENIR (§254) :
            // les retraits déjà en base hériteraient d'un débit nul, donc d'une
            // réservation à restituer de zéro franc si l'un d'eux échouait encore.
            //
            // On repose donc la valeur qui était VRAIE sous l'ancien modèle : le
            // débit valait le montant reçu, la sortie ayant été provisionnée dès
            // l'encaissement. L'historique dit ainsi vrai, et aucune lecture
            // n'a besoin d'un discriminant.
            migrationBuilder.Sql(
                "UPDATE \"Withdrawals\" SET \"WalletDebitedFcfa\" = \"AmountFcfa\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WalletDebitedFcfa",
                table: "Withdrawals");
        }
    }
}
