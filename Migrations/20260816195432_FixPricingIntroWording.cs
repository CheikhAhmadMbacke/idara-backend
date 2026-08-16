using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <summary>
    /// Correction de formulation, sans changement de schéma.
    ///
    /// Le texte d'introduction de la page publique des tarifs disait « les daara
    /// et écoles coraniques » — un pléonasme : un daara EST une école coranique.
    /// Le reste du site emploie déjà la forme « écoles coraniques (daara) », qui
    /// garde les deux mots (chacun est cherché sur Google) sans les opposer.
    ///
    /// ⚠️ UPDATE CONDITIONNEL : ne touche QUE le texte issu du seed d'origine.
    /// Si l'utilisateur a déjà réécrit son introduction depuis le back-office,
    /// elle est laissée intacte — une migration de contenu ne doit jamais
    /// écraser une saisie humaine.
    /// </summary>
    public partial class FixPricingIntroWording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "PricingPageContents"
                SET "Intro" = 'Idara équipe les écoles coraniques (daara) du Sénégal : élèves, classes, présences, suivi du Coran, paiement des mensualités et gestion financière. Le prix dépend uniquement du nombre d''élèves inscrits.'
                WHERE "Id" = 1
                  AND "Intro" LIKE '%daara et écoles coraniques%';
                """);

            migrationBuilder.Sql("""
                UPDATE "PricingPageContents"
                SET "IntroAr" = 'إدارة برنامج لتسيير المدارس القرآنية (الدارات) في السنغال: التلاميذ، الفصول، الحضور، متابعة القرآن، دفع الأقساط والتسيير المالي. السعر يعتمد فقط على عدد التلاميذ المسجلين.'
                WHERE "Id" = 1
                  AND "IntroAr" LIKE '%الدارات والمدارس القرآنية%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rien : on ne restaure pas volontairement un pléonasme.
        }
    }
}
