using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class SmsGovernanceAndOpsAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SmsMonthlyCapOverrideSegments",
                table: "Schools",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmsSuspended",
                table: "Schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SmsSuspendedAt",
                table: "Schools",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmsSuspendedReason",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SmsHardDailyCapFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SmsHardMonthlyCapFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SmsInternationalPriceCentimes",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "SmsKillSwitch",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SmsMaxMessagesPerDistinctRecipient",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsMaxPerRecipientPerDay",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsMaxPerRecipientPerMonth",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SmsMonthlyFeeHtFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SmsOffNetPriceCentimes",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SmsOnNetPriceCentimes",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "SmsRatioMinMessages",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsSchoolDailyFloorSegments",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsSchoolDailySegmentsPerStudent",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsSchoolHourlyFloorSegments",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsSchoolHourlySegmentsPerStudent",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsSchoolMonthlyFloorSegments",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsSchoolMonthlySegmentsPerStudent",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SmsSoftDailyCapFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SmsSoftMonthlyCapFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "SmsVatPercent",
                table: "PlatformSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "BlockedReason",
                table: "NotificationLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CharCount",
                table: "NotificationLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CostCentimes",
                table: "NotificationLogs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Encoding",
                table: "NotificationLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Network",
                table: "NotificationLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "NotificationLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "NotificationLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolNameSnapshot",
                table: "NotificationLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Segments",
                table: "NotificationLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SegmentsFixed160",
                table: "NotificationLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TriggerSource",
                table: "NotificationLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TriggerUserId",
                table: "NotificationLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UnitPriceCentimes",
                table: "NotificationLogs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "OpsAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    GroupingKey = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Advice = table.Column<string>(type: "text", nullable: true),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
                    RelatedId = table.Column<int>(type: "integer", nullable: true),
                    EmailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_Recipient_CreatedAt",
                table: "NotificationLogs",
                columns: new[] { "Recipient", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_SchoolId_CreatedAt",
                table: "NotificationLogs",
                columns: new[] { "SchoolId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpsAlerts_GroupingKey_EmailedAt",
                table: "OpsAlerts",
                columns: new[] { "GroupingKey", "EmailedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpsAlerts_Resolved_CreatedAt",
                table: "OpsAlerts",
                columns: new[] { "Resolved", "CreatedAt" });

            // ================================================================
            // 🔴 SEMER LA LIGNE DE REGLAGES EXISTANTE — SANS CECI, PLUS AUCUN
            // SMS NE PART EN PRODUCTION.
            //
            // EF genere `defaultValue: 0` pour chaque colonne ajoutee : la ligne
            // singleton deja presente en base heriterait donc de plafonds a ZERO,
            // et le garde-fou refuserait le premier envoi venu — codes de
            // connexion compris. Les valeurs par defaut ecrites dans le MODELE ne
            // valent que pour les lignes NOUVELLES ; celle-ci existe depuis
            // 2026-06. Meme piege de fond que le §624 : le fournisseur et le
            // modele ne parlent pas des memes lignes.
            //
            // Les valeurs ci-dessous doivent rester alignees sur celles de
            // PlatformSettings.cs. Elles sont toutes editables ensuite depuis
            // Reglages plateforme : ce n'est qu'un point de depart.
            //
            // `SmsBilingual` passe a FALSE dans le meme geste : mesure du
            // 2026-09-01, le bilingue coute x4 (l'arabe fait basculer tout le
            // corps en UCS-2, ou le segment ne vaut plus que 70 caracteres). La
            // regle d'or de NotificationService force de toute facon le bilingue
            // pour un compte JAMAIS connecte, donc le premier message — celui des
            // identifiants — reste dans les deux langues.
            // ================================================================
            migrationBuilder.Sql(@"
                UPDATE ""PlatformSettings"" SET
                    ""SmsBilingual"" = FALSE,
                    ""SmsOnNetPriceCentimes"" = 350,
                    ""SmsOffNetPriceCentimes"" = 500,
                    ""SmsInternationalPriceCentimes"" = 4035,
                    ""SmsMonthlyFeeHtFcfa"" = 10000,
                    ""SmsVatPercent"" = 18,
                    ""SmsKillSwitch"" = FALSE,
                    ""SmsSoftDailyCapFcfa"" = 8000,
                    ""SmsSoftMonthlyCapFcfa"" = 120000,
                    ""SmsHardDailyCapFcfa"" = 20000,
                    ""SmsHardMonthlyCapFcfa"" = 250000,
                    ""SmsSchoolMonthlySegmentsPerStudent"" = 10,
                    ""SmsSchoolMonthlyFloorSegments"" = 300,
                    ""SmsSchoolDailySegmentsPerStudent"" = 3,
                    ""SmsSchoolDailyFloorSegments"" = 150,
                    ""SmsSchoolHourlySegmentsPerStudent"" = 2,
                    ""SmsSchoolHourlyFloorSegments"" = 100,
                    ""SmsMaxMessagesPerDistinctRecipient"" = 3,
                    ""SmsRatioMinMessages"" = 20,
                    ""SmsMaxPerRecipientPerDay"" = 8,
                    ""SmsMaxPerRecipientPerMonth"" = 20
                WHERE ""Id"" = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpsAlerts");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_Recipient_CreatedAt",
                table: "NotificationLogs");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_SchoolId_CreatedAt",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "SmsMonthlyCapOverrideSegments",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SmsSuspended",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SmsSuspendedAt",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SmsSuspendedReason",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SmsHardDailyCapFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsHardMonthlyCapFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsInternationalPriceCentimes",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsKillSwitch",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsMaxMessagesPerDistinctRecipient",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsMaxPerRecipientPerDay",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsMaxPerRecipientPerMonth",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsMonthlyFeeHtFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsOffNetPriceCentimes",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsOnNetPriceCentimes",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsRatioMinMessages",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSchoolDailyFloorSegments",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSchoolDailySegmentsPerStudent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSchoolHourlyFloorSegments",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSchoolHourlySegmentsPerStudent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSchoolMonthlyFloorSegments",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSchoolMonthlySegmentsPerStudent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSoftDailyCapFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsSoftMonthlyCapFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsVatPercent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "CharCount",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "CostCentimes",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "Encoding",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "Network",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "SchoolNameSnapshot",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "Segments",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "SegmentsFixed160",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "TriggerSource",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "TriggerUserId",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "UnitPriceCentimes",
                table: "NotificationLogs");
        }
    }
}
