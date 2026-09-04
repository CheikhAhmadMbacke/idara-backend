using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class LegalMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegalAddress",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalCdpNumber",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalCompanyName",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalContactEmail",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalContactPhone",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalForm",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalNinea",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalRccm",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalRepresentative",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalVersion",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                // ⚠️ defaultValue CORRIGÉ À LA MAIN (§193, §202, §207 —
                // QUATRIÈME occurrence, cette fois sur une chaîne). EF génère
                // "" ; la ligne singleton réelle, qui existe depuis juin, en
                // hériterait. On horodaterait alors chaque acceptation avec une
                // version VIDE — c'est-à-dire sans savoir ce qui a été accepté,
                // exactement ce que ce champ existe pour empêcher.
                defaultValue: "2026-09");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LegalAddress",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalCdpNumber",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalCompanyName",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalContactEmail",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalContactPhone",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalForm",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalNinea",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalRccm",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalRepresentative",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LegalVersion",
                table: "PlatformSettings");
        }
    }
}
