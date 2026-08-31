using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class SchoolTypeAndPlanRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Schools",
                type: "integer",
                nullable: true);

            // Le plan le moins cher s'appelait « Daara ». Ce nom est PUBLIC
            // (idara.sn/plans) : une école franco-arabe qui le lit comprend que
            // l'offre n'est pas pour elle. Seul le NOM AFFICHÉ change — le code
            // « daara » reste, car SubscriptionExtensions.DefaultPlanCode s'y
            // appuie et le toucher casserait la souscription par défaut.
            //
            // Conditionnel : le seed ne s'applique qu'aux plans MANQUANTS, donc
            // en production le plan existe déjà avec l'ancien nom. Et le WHERE
            // sur l'ancien libellé évite d'écraser un nom que le SuperAdmin
            // aurait entre-temps choisi lui-même (précédent §154).
            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"Name\" = 'Essentiel' " +
                "WHERE \"Code\" = 'daara' AND \"Name\" = 'Daara';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Schools");

            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"Name\" = 'Daara' " +
                "WHERE \"Code\" = 'daara' AND \"Name\" = 'Essentiel';");
        }
    }
}
