using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class PalierB_GeneralFeeAndJournalScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GeneralMonthlyFeeFcfa",
                table: "SchoolPaymentSettings",
                type: "bigint",
                nullable: true);

            // Conversion one-shot des appréciations comportement/effort de
            // l'ancienne échelle /5 vers la nouvelle échelle 1-3
            // (1=Mauvais, 2=Moyen, 3=Bien). Mapping : 4-5 → 3, 3 → 2, 1-2 → 1.
            // Exécuté EXACTEMENT une fois (migration jouée une seule fois) — un
            // ré-run corromprait les données déjà converties, d'où le placement
            // ici et pas dans le code applicatif. Le CASE est évalué sur les
            // valeurs d'origine de façon atomique dans un seul UPDATE.
            migrationBuilder.Sql(
                "UPDATE \"DailyJournalEntries\" SET \"BehaviorScore\" = " +
                "CASE WHEN \"BehaviorScore\" >= 4 THEN 3 WHEN \"BehaviorScore\" = 3 THEN 2 ELSE 1 END " +
                "WHERE \"BehaviorScore\" IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE \"DailyJournalEntries\" SET \"EffortScore\" = " +
                "CASE WHEN \"EffortScore\" >= 4 THEN 3 WHEN \"EffortScore\" = 3 THEN 2 ELSE 1 END " +
                "WHERE \"EffortScore\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeneralMonthlyFeeFcfa",
                table: "SchoolPaymentSettings");
        }
    }
}
