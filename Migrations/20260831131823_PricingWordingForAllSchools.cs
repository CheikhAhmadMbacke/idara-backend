using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class PricingWordingForAllSchools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La page publique des tarifs est un CONTENU, éditable au
            // back-office : le seed ne s'applique qu'à la création, donc le
            // texte de production porte encore l'ancien vocabulaire.
            //
            // Conditionnel sur le texte exact (précédent §154) : si le
            // SuperAdmin a réécrit la page entre-temps, on ne l'écrase pas.
            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"HeroSubtitle\" = 'Un tarif par taille d''école. Sans engagement, résiliable à tout moment.' " +
                "WHERE \"HeroSubtitle\" = 'Un tarif par taille de daara. Sans engagement, résiliable à tout moment.';");

            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"Intro\" = 'Idara équipe les daara et les écoles franco-arabes du Sénégal : élèves, classes, présences, suivi du Coran, paiement des mensualités et gestion financière. Le prix dépend uniquement du nombre d''élèves inscrits.' " +
                "WHERE \"Intro\" = 'Idara équipe les écoles coraniques (daara) du Sénégal : élèves, classes, présences, suivi du Coran, paiement des mensualités et gestion financière. Le prix dépend uniquement du nombre d''élèves inscrits.';");

            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"HeroSubtitleAr\" = 'سعر حسب حجم المدرسة. دون التزام، ويمكن الإلغاء في أي وقت.' " +
                "WHERE \"HeroSubtitleAr\" = 'سعر حسب حجم الدارة. دون التزام، ويمكن الإلغاء في أي وقت.';");

            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"IntroAr\" = 'إدارة برنامج لتسيير المدارس القرآنية والفرنسية-العربية في السنغال: التلاميذ، الفصول، الحضور، متابعة القرآن، دفع الأقساط والتسيير المالي. السعر يعتمد فقط على عدد التلاميذ المسجلين.' " +
                "WHERE \"IntroAr\" = 'إدارة برنامج لتسيير المدارس القرآنية (الدارات) في السنغال: التلاميذ، الفصول، الحضور، متابعة القرآن، دفع الأقساط والتسيير المالي. السعر يعتمد فقط على عدد التلاميذ المسجلين.';");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"HeroSubtitle\" = 'Un tarif par taille de daara. Sans engagement, résiliable à tout moment.' " +
                "WHERE \"HeroSubtitle\" = 'Un tarif par taille d''école. Sans engagement, résiliable à tout moment.';");

            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"Intro\" = 'Idara équipe les écoles coraniques (daara) du Sénégal : élèves, classes, présences, suivi du Coran, paiement des mensualités et gestion financière. Le prix dépend uniquement du nombre d''élèves inscrits.' " +
                "WHERE \"Intro\" = 'Idara équipe les daara et les écoles franco-arabes du Sénégal : élèves, classes, présences, suivi du Coran, paiement des mensualités et gestion financière. Le prix dépend uniquement du nombre d''élèves inscrits.';");

            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"HeroSubtitleAr\" = 'سعر حسب حجم الدارة. دون التزام، ويمكن الإلغاء في أي وقت.' " +
                "WHERE \"HeroSubtitleAr\" = 'سعر حسب حجم المدرسة. دون التزام، ويمكن الإلغاء في أي وقت.';");

            migrationBuilder.Sql(
                "UPDATE \"PricingPageContents\" SET \"IntroAr\" = 'إدارة برنامج لتسيير المدارس القرآنية (الدارات) في السنغال: التلاميذ، الفصول، الحضور، متابعة القرآن، دفع الأقساط والتسيير المالي. السعر يعتمد فقط على عدد التلاميذ المسجلين.' " +
                "WHERE \"IntroAr\" = 'إدارة برنامج لتسيير المدارس القرآنية والفرنسية-العربية في السنغال: التلاميذ، الفصول، الحضور، متابعة القرآن، دفع الأقساط والتسيير المالي. السعر يعتمد فقط على عدد التلاميذ المسجلين.';");

        }
    }
}
