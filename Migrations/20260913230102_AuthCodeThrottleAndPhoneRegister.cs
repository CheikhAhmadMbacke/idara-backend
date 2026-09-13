using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class AuthCodeThrottleAndPhoneRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthCodeMaxPerIpPerDay",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthCodeMaxPerIpPerHour",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthCodeMaxPerRecipientPerDay",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthCodeMaxPerRecipientPerMonth",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthCodeMinSecondsBetween",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthCodeMinVerifyRatePercent",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthCodeVerifyRateMinSamples",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SenegalMobilePrefixes",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SmsAuthDailyCapFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AuthCodeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IpHash = table.Column<string>(type: "text", nullable: false),
                    Recipient = table.Column<string>(type: "text", nullable: false),
                    IsSms = table.Column<bool>(type: "boolean", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppCheckOk = table.Column<bool>(type: "boolean", nullable: false),
                    BlockedReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthCodeRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthCodeRequests_IpHash_CreatedAt",
                table: "AuthCodeRequests",
                columns: new[] { "IpHash", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthCodeRequests_IsSms_CreatedAt",
                table: "AuthCodeRequests",
                columns: new[] { "IsSms", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthCodeRequests_Recipient_CreatedAt",
                table: "AuthCodeRequests",
                columns: new[] { "Recipient", "CreatedAt" });

            // ================================================================
            // 🔴 §193/§202/§207/§232/§254/§255 — le piège du défaut à ZÉRO, ici
            // à la puissance NEUF. La ligne PlatformSettings existe depuis juin :
            // elle hérite des `defaultValue: 0` ci-dessus, jamais des valeurs par
            // défaut C#.
            //
            // Et cette fois le zéro ne serait pas « inoffensif jusqu'à ce qu'on
            // s'en aperçoive » — il serait ACTIF et absurde dans les deux sens :
            //   · AuthCodeMaxPerIpPerHour = 0  → aucune inscription possible,
            //     pour personne, dès le déploiement ;
            //   · AuthCodeMinSecondsBetween = 0 → aucun délai entre deux codes,
            //     la barrière la plus simple désarmée ;
            //   · SmsAuthDailyCapFcfa = 0      → canal SMS fermé en permanence ;
            //   · SenegalMobilePrefixes = ""   → aucun contrôle de préfixe.
            //
            // Les valeurs ci-dessous DOIVENT rester identiques aux défauts C# de
            // PlatformSettings : deux sources qui divergent, c'est un réglage qui
            // ne veut plus rien dire.
            // ================================================================
            migrationBuilder.Sql(@"
                UPDATE ""PlatformSettings"" SET
                    ""SenegalMobilePrefixes""            = '70,75,76,77,78',
                    ""SmsAuthDailyCapFcfa""              = 150,
                    ""AuthCodeMaxPerIpPerHour""          = 3,
                    ""AuthCodeMaxPerIpPerDay""           = 5,
                    ""AuthCodeMinSecondsBetween""        = 120,
                    ""AuthCodeMaxPerRecipientPerDay""    = 3,
                    ""AuthCodeMaxPerRecipientPerMonth""  = 5,
                    ""AuthCodeMinVerifyRatePercent""     = 30,
                    ""AuthCodeVerifyRateMinSamples""     = 10;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthCodeRequests");

            migrationBuilder.DropColumn(
                name: "AuthCodeMaxPerIpPerDay",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AuthCodeMaxPerIpPerHour",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AuthCodeMaxPerRecipientPerDay",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AuthCodeMaxPerRecipientPerMonth",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AuthCodeMinSecondsBetween",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AuthCodeMinVerifyRatePercent",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AuthCodeVerifyRateMinSamples",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SenegalMobilePrefixes",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmsAuthDailyCapFcfa",
                table: "PlatformSettings");
        }
    }
}
