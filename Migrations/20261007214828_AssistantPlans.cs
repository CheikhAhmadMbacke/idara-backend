using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class AssistantPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 🔴 ÉCRIT À LA MAIN. EF avait généré un RenameColumn de
            // AssistantOutputPriceCentimesPerMTok vers AiUsdRateFcfa : la valeur
            // 1 214 000 aurait été gardée, soit un dollar à 1,2 million de
            // francs — et le plafond journalier aurait coupé l'assistant au
            // premier message (§255). On SUPPRIME et on AJOUTE, valeurs semées.
            migrationBuilder.DropColumn(name: "AssistantCacheReadPriceCentimesPerMTok", table: "PlatformSettings");
            migrationBuilder.DropColumn(name: "AssistantInputPriceCentimesPerMTok", table: "PlatformSettings");
            migrationBuilder.DropColumn(name: "AssistantOutputPriceCentimesPerMTok", table: "PlatformSettings");

            // Taux RÉEL d'un dollar Anthropic, frais Wave compris (relevés de
            // Cheikh : 615,9 et 609,8 F) — le plus défavorable.
            migrationBuilder.AddColumn<long>(
                name: "AiUsdRateFcfa", table: "PlatformSettings",
                type: "bigint", nullable: false, defaultValue: 616L);

            // « Usage raisonnable » de l'illimité Grand (§193 : semé, pas 0).
            migrationBuilder.AddColumn<int>(
                name: "AssistantDailySchoolCap", table: "PlatformSettings",
                type: "integer", nullable: false, defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "IncludedCommands", table: "AssistantTurns",
                type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "AssistantCreditPriceFcfa", table: "SubscriptionPlans",
                type: "bigint", nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "AssistantIncludedCommands", table: "SubscriptionPlans",
                type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(
                name: "AssistantUnlimited", table: "SubscriptionPlans",
                type: "boolean", nullable: false, defaultValue: false);

            // Plafond plateforme relevé : l'illimité Grand le sollicitera.
            // Seulement s'il n'a pas été touché à la main.
            migrationBuilder.Sql(@"UPDATE ""PlatformSettings""
                SET ""AssistantDailyPlatformCapFcfa"" = 25000
                WHERE ""AssistantDailyPlatformCapFcfa"" = 10000;");

            // Lecture de cahier : même taux réel (607 → 616 F/$), sur Opus 5
            // (5 $ / 25 $). Seulement si les valeurs sont encore celles d'origine.
            migrationBuilder.Sql(@"UPDATE ""PlatformSettings""
                SET ""OcrInputPriceCentimesPerMTok"" = 308000
                WHERE ""OcrInputPriceCentimesPerMTok"" = 303500;");
            migrationBuilder.Sql(@"UPDATE ""PlatformSettings""
                SET ""OcrOutputPriceCentimesPerMTok"" = 1540000
                WHERE ""OcrOutputPriceCentimesPerMTok"" = 1517500;");

            // Décision de Cheikh (2026-10-07) : l'assistant est le levier vers
            // Pro et Grand. Prix des plans INCHANGÉS.
            migrationBuilder.Sql(@"UPDATE ""SubscriptionPlans""
                SET ""AssistantIncludedCommands"" = 400, ""AssistantCreditPriceFcfa"" = 40
                WHERE ""Code"" = 'pro' AND NOT ""IsCustom"";");
            migrationBuilder.Sql(@"UPDATE ""SubscriptionPlans""
                SET ""AssistantUnlimited"" = TRUE
                WHERE ""Code"" = 'grand' AND NOT ""IsCustom"";");

            // La page publique /plans le DIT. Une ligne par plan public, en tête
            // de liste d'ajouts, seulement si aucune ligne « Assistant » n'existe.
            foreach (var (code, fr, ar) in new[]
            {
                ("daara", "Assistant IA : 20 commandes offertes, puis 50 F la commande",
                          "المساعد الذكي: 20 أمرا مجانا، ثم ⁦FCFA 50⁩ للأمر"),
                ("standard", "Assistant IA : 20 commandes offertes, puis 50 F la commande",
                             "المساعد الذكي: 20 أمرا مجانا، ثم ⁦FCFA 50⁩ للأمر"),
                ("pro", "Assistant IA inclus : 400 commandes par mois",
                        "المساعد الذكي مشمول: 400 أمر في الشهر"),
                ("grand", "Assistant IA illimité",
                          "المساعد الذكي بلا حدود"),
            })
            {
                migrationBuilder.Sql($@"INSERT INTO ""SubscriptionPlanFeatures""
                    (""PlanId"", ""Label"", ""LabelAr"", ""Included"", ""DisplayOrder"", ""CreatedAt"")
                    SELECT p.""Id"", '{fr}', '{ar}', TRUE,
                           COALESCE((SELECT MAX(f.""DisplayOrder"") FROM ""SubscriptionPlanFeatures"" f WHERE f.""PlanId"" = p.""Id""), 0) + 1,
                           NOW() AT TIME ZONE 'UTC'
                    FROM ""SubscriptionPlans"" p
                    WHERE p.""Code"" = '{code}' AND NOT p.""IsCustom""
                      AND NOT EXISTS (SELECT 1 FROM ""SubscriptionPlanFeatures"" f
                                      WHERE f.""PlanId"" = p.""Id"" AND f.""Label"" LIKE 'Assistant IA%');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AssistantCreditPriceFcfa", table: "SubscriptionPlans");
            migrationBuilder.DropColumn(name: "AssistantIncludedCommands", table: "SubscriptionPlans");
            migrationBuilder.DropColumn(name: "AssistantUnlimited", table: "SubscriptionPlans");
            migrationBuilder.DropColumn(name: "AssistantDailySchoolCap", table: "PlatformSettings");
            migrationBuilder.DropColumn(name: "IncludedCommands", table: "AssistantTurns");
            migrationBuilder.DropColumn(name: "AiUsdRateFcfa", table: "PlatformSettings");
            migrationBuilder.AddColumn<long>(name: "AssistantInputPriceCentimesPerMTok", table: "PlatformSettings",
                type: "bigint", nullable: false, defaultValue: 242800L);
            migrationBuilder.AddColumn<long>(name: "AssistantOutputPriceCentimesPerMTok", table: "PlatformSettings",
                type: "bigint", nullable: false, defaultValue: 1214000L);
            migrationBuilder.AddColumn<long>(name: "AssistantCacheReadPriceCentimesPerMTok", table: "PlatformSettings",
                type: "bigint", nullable: false, defaultValue: 12140L);
            migrationBuilder.Sql(@"DELETE FROM ""SubscriptionPlanFeatures"" WHERE ""Label"" LIKE 'Assistant IA%';");
        }
    }
}
