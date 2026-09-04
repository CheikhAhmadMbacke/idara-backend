using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class DonationCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifySchoolBySmsOnPayment",
                table: "SchoolPaymentSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ⚠️ defaultValue CORRIGÉ À LA MAIN (§193, §202 — troisième occurrence).
            // EF génère `defaultValue: 0L`. PlatformSettings est une ligne
            // SINGLETON qui existe en production depuis juin : elle n'est pas
            // recréée par cette migration, elle hérite du défaut de la colonne.
            // Avec 0, le plafond des dons vaudrait zéro et la page publique
            // refuserait TOUS les dons — fonctionnalité morte-née, invisible à la
            // compilation comme aux tests sur base neuve (où le défaut C# de
            // l'entité s'applique, lui, et masque le problème).
            migrationBuilder.AddColumn<long>(
                name: "MaxDonationFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 2_000_000L);

            migrationBuilder.AddColumn<long>(
                name: "MinDonationFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 500L);

            migrationBuilder.AddColumn<int>(
                name: "DonationCampaignId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DonorAnonymous",
                table: "Payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DonorName",
                table: "Payments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DonorOrganization",
                table: "Payments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DonorPhone",
                table: "Payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DonationCampaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CoverImagePath = table.Column<string>(type: "text", nullable: true),
                    GoalAmountFcfa = table.Column<long>(type: "bigint", nullable: true),
                    AmountMode = table.Column<int>(type: "integer", nullable: false),
                    FixedAmountFcfa = table.Column<long>(type: "bigint", nullable: true),
                    SuggestedAmountsCsv = table.Column<string>(type: "text", nullable: true),
                    FeesPayer = table.Column<int>(type: "integer", nullable: false),
                    ShowDonorWall = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosesAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPermanent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedById = table.Column<int>(type: "integer", nullable: true),
                    OpenCount = table.Column<int>(type: "integer", nullable: false),
                    LastOpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DonationCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DonationCampaigns_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_DonationCampaignId",
                table: "Payments",
                column: "DonationCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_DonationCampaigns_SchoolId",
                table: "DonationCampaigns",
                column: "SchoolId",
                unique: true,
                filter: "\"IsPermanent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_DonationCampaigns_Slug",
                table: "DonationCampaigns",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_DonationCampaigns_DonationCampaignId",
                table: "Payments",
                column: "DonationCampaignId",
                principalTable: "DonationCampaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_DonationCampaigns_DonationCampaignId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "DonationCampaigns");

            migrationBuilder.DropIndex(
                name: "IX_Payments_DonationCampaignId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "NotifySchoolBySmsOnPayment",
                table: "SchoolPaymentSettings");

            migrationBuilder.DropColumn(
                name: "MaxDonationFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "MinDonationFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "DonationCampaignId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DonorAnonymous",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DonorName",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DonorOrganization",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DonorPhone",
                table: "Payments");
        }
    }
}
