using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class PaymentLinkFirstOpenedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstOpenedAt",
                table: "PaymentLinks",
                type: "timestamp with time zone",
                nullable: true);

            // Rattrapage des liens DEJA ouverts avant cette colonne.
            //
            // Sans lui, un lien ouvert en aout puis rouvert apres la campagne
            // verrait sa « premiere ouverture » datee d'apres le SMS : la
            // campagne s'attribuerait une ouverture qu'elle n'a pas provoquee.
            // On recopie donc la derniere ouverture connue. C'est EXACT quand le
            // lien n'a ete ouvert qu'une fois, et sinon c'est une borne basse —
            // dans les deux cas cela marque ce qui compte : « ce lien etait deja
            // connu avant la campagne ».
            migrationBuilder.Sql(@"
                UPDATE ""PaymentLinks""
                SET ""FirstOpenedAt"" = ""LastOpenedAt""
                WHERE ""LastOpenedAt"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstOpenedAt",
                table: "PaymentLinks");
        }
    }
}
