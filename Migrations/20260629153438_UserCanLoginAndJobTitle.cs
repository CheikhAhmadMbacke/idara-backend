using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class UserCanLoginAndJobTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue true : tous les comptes EXISTANTS gardent l'accès à
            // l'app (le défaut CLR `false` aurait bloqué tout le monde au login).
            // Les comptes « sans appli » posent CanLogin=false explicitement.
            migrationBuilder.AddColumn<bool>(
                name: "CanLogin",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanLogin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "Users");
        }
    }
}
