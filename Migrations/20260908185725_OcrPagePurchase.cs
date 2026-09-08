using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class OcrPagePurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OcrDefaultStudentsPerPage",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OcrMaxPagesPerPurchase",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "OcrPriceBaseFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OcrPricePerStudentFcfa",
                table: "PlatformSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "OcrPurchaseEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OcrPagesPurchased",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "OcrPricePerPageFcfa",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AmountFcfa",
                table: "OcrPageGrants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "PaymentId",
                table: "OcrPageGrants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PricePerPageFcfa",
                table: "OcrPageGrants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_OcrPageGrants_PaymentId",
                table: "OcrPageGrants",
                column: "PaymentId",
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OcrPageGrants_Payments_PaymentId",
                table: "OcrPageGrants",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 🔴 CINQUIÈME occurrence du piège du `defaultValue` d'EF (§193,
            // §202, §207, et `FamilyId`). Elle est la plus coûteuse des cinq, et
            // elle vise UNE SEULE LIGNE : le singleton `PlatformSettings`, créé
            // en juin. Cette ligne EXISTE — elle n'est pas recréée au démarrage,
            // donc les valeurs par défaut du modèle C# ne s'y appliquent JAMAIS.
            // Elle hérite du défaut de COLONNE, écrit juste au-dessus :
            //
            //   OcrPurchaseEnabled      = false → la vente est morte-née ;
            //   OcrPriceBaseFcfa        = 0     ⎫ prix d'une page = 0 :
            //   OcrPricePerStudentFcfa  = 0     ⎭ les pages seraient GRATUITES ;
            //   OcrMaxPagesPerPurchase  = 0     → tout achat refusé ;
            //   OcrDefaultStudentsPerPage = 0   → repli à 0 élève par page.
            //
            // Aucun de ces défauts ne se voit à la compilation, ni sur une base
            // NEUVE — où le défaut C# de l'entité s'applique et masque tout.
            // Seule une base ayant déjà la ligne les révèle : c'est-à-dire la
            // production, et elle seule.
            //
            // Le WHERE n'écrase que ce qui vaut le défaut de colonne : rejouer
            // cette migration ne peut donc pas défaire un réglage que le
            // SuperAdmin aurait modifié entre-temps.
            migrationBuilder.Sql("""
                UPDATE "PlatformSettings"
                   SET "OcrPurchaseEnabled"        = TRUE  WHERE "OcrPurchaseEnabled" = FALSE;
                UPDATE "PlatformSettings"
                   SET "OcrPriceBaseFcfa"          = 30    WHERE "OcrPriceBaseFcfa" = 0;
                UPDATE "PlatformSettings"
                   SET "OcrPricePerStudentFcfa"    = 3     WHERE "OcrPricePerStudentFcfa" = 0;
                UPDATE "PlatformSettings"
                   SET "OcrDefaultStudentsPerPage" = 15    WHERE "OcrDefaultStudentsPerPage" = 0;
                UPDATE "PlatformSettings"
                   SET "OcrMaxPagesPerPurchase"    = 300   WHERE "OcrMaxPagesPerPurchase" = 0;
                """);

            // 🔴 LA MÊME LEÇON, SOUS UNE AUTRE FORME — et celle-ci ne concerne
            // PAS une colonne nouvelle. `OcrMaxPagesPerRequest` existe depuis le
            // 2026-09-02 et vaut 12 en base. Relever le défaut C# à 40 ne change
            // RIEN à la ligne déjà écrite : un défaut d'entité ne s'applique
            // qu'à une ligne qu'on crée.
            //
            // Sans cette ligne, la prise en charge du PDF serait plafonnée à
            // 12 pages en production — soit exactement la limite qu'elle est
            // censée lever, puisqu'un ancien cahier scanné en fait couramment
            // trente ou quarante. Fonctionnalité livrée, et inopérante.
            //
            // Le WHERE ne touche que la valeur historique : un réglage ajusté
            // depuis l'écran SuperAdmin est préservé.
            migrationBuilder.Sql("""
                UPDATE "PlatformSettings"
                   SET "OcrMaxPagesPerRequest" = 40 WHERE "OcrMaxPagesPerRequest" = 12;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OcrPageGrants_Payments_PaymentId",
                table: "OcrPageGrants");

            migrationBuilder.DropIndex(
                name: "IX_OcrPageGrants_PaymentId",
                table: "OcrPageGrants");

            migrationBuilder.DropColumn(
                name: "OcrDefaultStudentsPerPage",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrMaxPagesPerPurchase",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrPriceBaseFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrPricePerStudentFcfa",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrPurchaseEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OcrPagesPurchased",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "OcrPricePerPageFcfa",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AmountFcfa",
                table: "OcrPageGrants");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "OcrPageGrants");

            migrationBuilder.DropColumn(
                name: "PricePerPageFcfa",
                table: "OcrPageGrants");
        }
    }
}
