using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class LegalVersion20261012 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CGU §2, §9.2, §9.3, §11 : la commission « prélevée par le prestataire »
            // devient des « frais de service » appliqués par l'éditeur (conseil de
            // Wave, 2026-10-12). La version affichée suit — seulement si elle n'a pas
            // déjà été changée à la main.
            migrationBuilder.Sql(@"UPDATE ""PlatformSettings"" SET ""LegalVersion"" = '2026-10-12' WHERE ""LegalVersion"" = '2026-10';");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""PlatformSettings"" SET ""LegalVersion"" = '2026-10' WHERE ""LegalVersion"" = '2026-10-12';");

        }
    }
}
