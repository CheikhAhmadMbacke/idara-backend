using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class SearchFoldAndAjami : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 🔑 EN PREMIER, et ce n'est pas un détail d'ordre : c'est cette
            // extension qui rend « Mbacke » et « Mbacké » équivalents, dans les
            // 63 recherches de l'application. Si elle manquait, chaque
            // recherche lèverait une erreur SQL — donc elle est créée avant
            // tout le reste, et la migration échoue franchement si elle ne peut
            // pas l'être, plutôt que de laisser partir une version dont la
            // recherche est morte.
            //
            // « unaccent » est « trusted » depuis PostgreSQL 13 : le
            // PROPRIÉTAIRE de la base suffit, il n'y a pas besoin d'être
            // superuser. Vérifié en production le 2026-09-15 sur PG 16.15 avec
            // le compte « idara » (CREATE EXTENSION dans une transaction
            // annulée, pour ne rien laisser derrière).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");

            migrationBuilder.AddColumn<string>(
                name: "SearchIndex",
                table: "Users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchIndex",
                table: "Students",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchIndex",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SearchIndex",
                table: "Students");

            // L'extension n'est PAS supprimée : rien ne dit qu'elle n'a pas été
            // installée avant nous ni qu'une autre requête ne s'en sert, et un
            // DROP EXTENSION en cascade sur un retour arrière ferait des dégâts
            // bien plus grands que la colonne qu'on annule.
        }
    }
}
