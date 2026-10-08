using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class LegalVersion202610 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CGU §15 et confidentialité §7, §8, §9, §16 : Anthropic nommé comme
            // prestataire d'IA, l'assistant de l'école décrit (2026-10-08). La
            // version affichée en tête des pages suit — seulement si elle n'a pas
            // déjà été changée à la main.
            migrationBuilder.Sql(@"UPDATE ""PlatformSettings"" SET ""LegalVersion"" = '2026-10' WHERE ""LegalVersion"" = '2026-09';");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""PlatformSettings"" SET ""LegalVersion"" = '2026-09' WHERE ""LegalVersion"" = '2026-10';");

        }
    }
}
