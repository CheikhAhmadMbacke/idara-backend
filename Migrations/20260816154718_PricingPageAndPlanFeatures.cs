using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Idara.API.Migrations
{
    /// <inheritdoc />
    public partial class PricingPageAndPlanFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsHighlighted",
                table: "SubscriptionPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ⚠️ defaultValue TRUE (et non le false généré par défaut) : sans ça,
            // les 4 plans publics déjà en base arriveraient à false et la page
            // /plans s'afficherait VIDE au déploiement — sans la moindre erreur,
            // donc sans que rien ne le signale.
            migrationBuilder.AddColumn<bool>(
                name: "IsPubliclyListed",
                table: "SubscriptionPlans",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Les deals négociés ne sont jamais listés publiquement : on aligne
            // la donnée sur la règle, plutôt que de compter uniquement sur le
            // filtre de lecture. Idempotent (§74).
            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"IsPubliclyListed\" = FALSE WHERE \"IsCustom\" = TRUE;");

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "SubscriptionPlans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tagline",
                table: "SubscriptionPlans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaglineAr",
                table: "SubscriptionPlans",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PricingFaqs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Question = table.Column<string>(type: "text", nullable: false),
                    QuestionAr = table.Column<string>(type: "text", nullable: true),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    AnswerAr = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingFaqs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingPageContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    HeroTitle = table.Column<string>(type: "text", nullable: false),
                    HeroTitleAr = table.Column<string>(type: "text", nullable: true),
                    HeroSubtitle = table.Column<string>(type: "text", nullable: false),
                    HeroSubtitleAr = table.Column<string>(type: "text", nullable: true),
                    Intro = table.Column<string>(type: "text", nullable: true),
                    IntroAr = table.Column<string>(type: "text", nullable: true),
                    WhatsappNumber = table.Column<string>(type: "text", nullable: true),
                    ContactEmail = table.Column<string>(type: "text", nullable: true),
                    FooterNote = table.Column<string>(type: "text", nullable: true),
                    FooterNoteAr = table.Column<string>(type: "text", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingPageContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LabelAr = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanFeatures_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingFaqs_DisplayOrder",
                table: "PricingFaqs",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanFeatures_PlanId_DisplayOrder",
                table: "SubscriptionPlanFeatures",
                columns: new[] { "PlanId", "DisplayOrder" });

            // ---------------------------------------------------------------
            // Contenu de départ : la page doit être présentable dès le premier
            // jour, sinon elle reste vide jusqu'à ce qu'on pense à la remplir.
            // Tout est modifiable ensuite depuis le back-office.
            //
            // Placé dans la MIGRATION et non dans un seed applicatif : un seed
            // rejoué à chaque démarrage recréerait ce que l'utilisateur a
            // volontairement supprimé (§74).
            // ---------------------------------------------------------------
            migrationBuilder.Sql("""
                INSERT INTO "PricingPageContents"
                    ("Id","HeroTitle","HeroTitleAr","HeroSubtitle","HeroSubtitleAr",
                     "Intro","IntroAr","WhatsappNumber","ContactEmail",
                     "FooterNote","FooterNoteAr","IsPublished","UpdatedAt")
                VALUES (1,
                    'Nos formules',
                    'خططنا',
                    'Un tarif par taille de daara. Sans engagement, résiliable à tout moment.',
                    'سعر حسب حجم الدارة. دون التزام، ويمكن الإلغاء في أي وقت.',
                    'Idara équipe les écoles coraniques (daara) du Sénégal : élèves, classes, présences, suivi du Coran, paiement des mensualités et gestion financière. Le prix dépend uniquement du nombre d''élèves inscrits.',
                    'إدارة برنامج لتسيير المدارس القرآنية (الدارات) في السنغال: التلاميذ، الفصول، الحضور، متابعة القرآن، دفع الأقساط والتسيير المالي. السعر يعتمد فقط على عدد التلاميذ المسجلين.',
                    '+221 76 363 53 27',
                    'contact.pyranil@gmail.com',
                    'Essai gratuit de 30 jours. Aucun engagement de durée.',
                    'تجربة مجانية لمدة 30 يوما. دون أي التزام.',
                    TRUE, NOW())
                ON CONFLICT ("Id") DO NOTHING;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "PricingFaqs"
                    ("Question","QuestionAr","Answer","AnswerAr","DisplayOrder","IsActive","CreatedAt")
                VALUES
                ('Puis-je changer de formule plus tard ?',
                 'هل يمكنني تغيير الخطة لاحقا؟',
                 'Oui, à tout moment depuis votre espace. Le nouveau tarif s''applique au prochain prélèvement.',
                 'نعم، في أي وقت من حسابك. يطبق السعر الجديد عند الاستقطاع القادم.',
                 1, TRUE, NOW()),
                ('Que se passe-t-il si mon daara dépasse le nombre d''élèves de ma formule ?',
                 'ماذا يحدث إذا تجاوزت دارتي عدد التلاميذ في خطتي؟',
                 'Vous n''êtes jamais bloqué : vous pouvez toujours inscrire un élève. Votre abonnement passe simplement à la formule correspondante au prochain prélèvement, et vous en êtes prévenu.',
                 'لن يتم منعك أبدا: يمكنك دائما تسجيل تلميذ جديد. ينتقل اشتراكك تلقائيا إلى الخطة المناسبة عند الاستقطاع القادم، مع إشعارك بذلك.',
                 2, TRUE, NOW()),
                ('Comment se fait le paiement de l''abonnement ?',
                 'كيف يتم دفع الاشتراك؟',
                 'Il est prélevé chaque mois sur le portefeuille du daara, alimenté par les paiements des parents ou par une recharge directe.',
                 'يتم استقطاعه شهريا من محفظة الدارة، التي تتغذى من مدفوعات الأولياء أو من شحن مباشر.',
                 3, TRUE, NOW()),
                ('L''application fonctionne-t-elle en arabe ?',
                 'هل يعمل التطبيق باللغة العربية؟',
                 'Oui. Toute l''application existe en français et en arabe, avec l''affichage de droite à gauche.',
                 'نعم. التطبيق كامل متوفر بالفرنسية والعربية، مع العرض من اليمين إلى اليسار.',
                 4, TRUE, NOW());
                """);

            // Avantages des plans publics existants. Conditionnel : rien n'est
            // inséré pour un plan qui en a déjà, et aucun échec s'il n'y a
            // aucun plan.
            // ⓘ Sur une base VIERGE, les migrations tournent AVANT le seed des
            // plans (DbInitializer) : aucun avantage n'est donc créé, et c'est
            // sans conséquence — ils se saisissent au back-office. En production
            // les 4 plans existent depuis juin 2026, ils sont bien servis.
            migrationBuilder.Sql("""
                INSERT INTO "SubscriptionPlanFeatures"
                    ("PlanId","Label","LabelAr","Included","DisplayOrder","CreatedAt")
                SELECT p."Id", f."Label", f."LabelAr", TRUE, f."Ord", NOW()
                FROM "SubscriptionPlans" p
                CROSS JOIN (VALUES
                    ('Élèves, classes et responsables', 'التلاميذ والفصول والأولياء', 1),
                    ('Présences et suivi du Coran', 'الحضور ومتابعة القرآن', 2),
                    ('Paiement des mensualités en ligne', 'دفع الأقساط عبر الإنترنت', 3),
                    ('Notes et bulletins', 'النقاط وكشوف الدرجات', 4),
                    ('Gestion financière du daara', 'التسيير المالي للدارة', 5),
                    ('Application Android et web', 'تطبيق أندرويد وويب', 6),
                    ('Français et arabe', 'الفرنسية والعربية', 7)
                ) AS f("Label","LabelAr","Ord")
                WHERE p."IsCustom" = FALSE
                  AND NOT EXISTS (
                      SELECT 1 FROM "SubscriptionPlanFeatures" x WHERE x."PlanId" = p."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PricingFaqs");

            migrationBuilder.DropTable(
                name: "PricingPageContents");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanFeatures");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IsHighlighted",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IsPubliclyListed",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Tagline",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "TaglineAr",
                table: "SubscriptionPlans");
        }
    }
}
