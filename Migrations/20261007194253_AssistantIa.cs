using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class AssistantIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ Les colonnes de PlatformSettings sont SEMÉES avec les vraies
            // valeurs (§193/§254) : EF générait 0/false, ce qui aurait fait
            // naître l'assistant éteint, gratuit et sans plafond.
            migrationBuilder.AddColumn<long>(
                name: "AssistantCacheReadPriceCentimesPerMTok",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 12140L);

            migrationBuilder.AddColumn<long>(
                name: "AssistantDailyPlatformCapFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 10000L);

            migrationBuilder.AddColumn<bool>(
                name: "AssistantEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AssistantFreeCommands",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<long>(
                name: "AssistantInputPriceCentimesPerMTok",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 242800L);

            migrationBuilder.AddColumn<int>(
                name: "AssistantMaxCommandsPerPurchase",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "AssistantMaxCommandsPerUserPerHour",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<long>(
                name: "AssistantOutputPriceCentimesPerMTok",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 1214000L);

            migrationBuilder.AddColumn<long>(
                name: "AssistantPricePerCommandFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 50L);

            migrationBuilder.AddColumn<bool>(
                name: "AssistantPurchaseEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AssistantCommandsPurchased",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "AssistantPricePerCommandFcfa",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AssistantActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TurnId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    SummaryJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultMessage = table.Column<string>(type: "text", nullable: true),
                    ResultEntityId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantActions_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantCreditGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Commands = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PaymentId = table.Column<int>(type: "integer", nullable: true),
                    PricePerCommandFcfa = table.Column<long>(type: "bigint", nullable: false),
                    AmountFcfa = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantCreditGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantCreditGrants_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssistantCreditGrants_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantTurns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Reply = table.Column<string>(type: "text", nullable: true),
                    ChargedCommands = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    BlockedReason = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: false),
                    Rounds = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CacheReadTokens = table.Column<int>(type: "integer", nullable: false),
                    CacheWriteTokens = table.Column<int>(type: "integer", nullable: false),
                    CostCentimes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantTurns_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantActions_SchoolId_CreatedAt",
                table: "AssistantActions",
                columns: new[] { "SchoolId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantCreditGrants_PaymentId",
                table: "AssistantCreditGrants",
                column: "PaymentId",
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantCreditGrants_SchoolId",
                table: "AssistantCreditGrants",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTurns_CreatedAt",
                table: "AssistantTurns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTurns_SchoolId_CreatedAt",
                table: "AssistantTurns",
                columns: new[] { "SchoolId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantActions");

            migrationBuilder.DropTable(
                name: "AssistantCreditGrants");

            migrationBuilder.DropTable(
                name: "AssistantTurns");

            migrationBuilder.DropColumn(
                name: "AssistantCacheReadPriceCentimesPerMTok",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantDailyPlatformCapFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantFreeCommands",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantInputPriceCentimesPerMTok",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantMaxCommandsPerPurchase",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantMaxCommandsPerUserPerHour",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantOutputPriceCentimesPerMTok",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantPricePerCommandFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantPurchaseEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AssistantCommandsPurchased",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AssistantPricePerCommandFcfa",
                table: "Payments");
        }
    }
}
