using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase2UserUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normalisation one-shot (pattern §74) : les emails sont déjà écrits en
            // lowercase depuis le fix §95, mais d'anciens comptes pré-fix peuvent
            // avoir une casse mixte. On les abaisse AVANT de créer l'index brut, sinon
            // « A@x » et « a@x » seraient considérés distincts par l'index alors que le
            // login les traite comme identiques. Idempotent (le WHERE exclut le déjà-bas).
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"Email\" = lower(\"Email\") " +
                "WHERE \"Email\" IS NOT NULL AND \"Email\" <> lower(\"Email\");");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users",
                column: "PhoneNumber",
                unique: true,
                filter: "\"PhoneNumber\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users");
        }
    }
}
