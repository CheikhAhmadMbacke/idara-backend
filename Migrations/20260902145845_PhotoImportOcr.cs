using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class PhotoImportOcr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OcrBaseAllowancePages",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "OcrDailyPlatformCapFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "OcrEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "OcrInputPriceCentimesPerMTok",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "OcrMaxConsecutiveFailures",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OcrMaxPagesPerRequest",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "OcrOutputPriceCentimesPerMTok",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "OcrJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    ChargedPages = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    BlockedReason = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CostCentimes = table.Column<long>(type: "bigint", nullable: false),
                    ExtractedRows = table.Column<int>(type: "integer", nullable: false),
                    UncertainCells = table.Column<int>(type: "integer", nullable: false),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcrJobs_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OcrPageGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Pages = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrPageGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcrPageGrants_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OcrJobs_CreatedAt",
                table: "OcrJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OcrJobs_SchoolId_CreatedAt",
                table: "OcrJobs",
                columns: new[] { "SchoolId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OcrPageGrants_SchoolId",
                table: "OcrPageGrants",
                column: "SchoolId");

            // 🔴 §193 — SEMIS OBLIGATOIRE de la ligne singleton EXISTANTE.
            //
            // EF a genere `defaultValue: false` / `0` ci-dessus. Ces defauts ne
            // s'appliquent qu'aux lignes NOUVELLES ; les valeurs par defaut
            // ecrites dans le modele C# ne touchent jamais une ligne deja en
            // base. La ligne PlatformSettings, creee en juin, recevrait donc
            // OcrEnabled = false et TOUS les plafonds a zero — l'import par
            // photo serait mort-ne en production, sans le moindre message.
            //
            // Exactement le piege du 2026-09-01 sur les plafonds SMS. Les
            // valeurs ci-dessous sont alignees sur celles du modele.
            migrationBuilder.Sql("""
                UPDATE "PlatformSettings"
                SET "OcrEnabled" = TRUE,
                    "OcrBaseAllowancePages" = 30,
                    "OcrMaxPagesPerRequest" = 12,
                    "OcrDailyPlatformCapFcfa" = 25000,
                    "OcrMaxConsecutiveFailures" = 5,
                    "OcrInputPriceCentimesPerMTok" = 303500,
                    "OcrOutputPriceCentimesPerMTok" = 1517500;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OcrJobs");

            migrationBuilder.DropTable(
                name: "OcrPageGrants");

            migrationBuilder.DropColumn(
                name: "OcrBaseAllowancePages",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrDailyPlatformCapFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrInputPriceCentimesPerMTok",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrMaxConsecutiveFailures",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrMaxPagesPerRequest",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrOutputPriceCentimesPerMTok",
                table: "PlatformSettings");
        }
    }
}
