using System.Net;
using System.Text;
using Idara.API.DTOs.Subscription;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Rend la page publique des tarifs (idara.sn/plans) en HTML complet, côté
    /// SERVEUR.
    ///
    /// Pourquoi pas un écran de l'application : cette page est faite pour être
    /// ENVOYÉE à un prospect. Rendue côté serveur, elle est lisible par Google,
    /// affiche un aperçu quand le lien est collé dans WhatsApp, s'ouvre en une
    /// seconde sur un forfait modeste et s'imprime en PDF depuis le navigateur.
    /// Une page rendue en JavaScript ne fait aucune de ces quatre choses.
    ///
    /// Aucune ressource externe (police, script, feuille de style) : tout est
    /// dans la page. C'est aussi ce qui la rend instantanée.
    ///
    /// Rendu PUBLIC et statique exprès : une page qu'on ne peut vérifier qu'en
    /// la déployant ne se vérifie jamais (même discipline que les reçus rendus
    /// en image et l'e-mail d'incident, §116/§133).
    /// </summary>
    public static class PricingHtmlRenderer
    {
        public const string SiteUrl = "https://idara.sn";
        public const string PageUrl = SiteUrl + "/plans";

        /// <summary>Rend la page. <paramref name="lang"/> = « fr » ou « ar ».</summary>
        public static string Render(PublicPricingDto data, string lang = "fr")
        {
            var ar = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase);
            var c = data.Content;

            var title = Pick(c.HeroTitle, c.HeroTitleAr, ar);
            var subtitle = Pick(c.HeroSubtitle, c.HeroSubtitleAr, ar);
            var intro = Pick(c.Intro, c.IntroAr, ar);
            var footer = Pick(c.FooterNote, c.FooterNoteAr, ar);

            var sb = new StringBuilder(16_000);
            sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(ar ? "ar" : "fr")
              .Append("\" dir=\"").Append(ar ? "rtl" : "ltr").Append("\">\n<head>\n");
            sb.Append("<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");

            // Le titre de l'onglet et le référencement portent la marque et le
            // métier : c'est cette page qu'on veut voir sortir sur « prix
            // logiciel gestion daara ».
            var metaTitle = ar
                ? "أسعار إدارة — برنامج إدارة الدارات"
                : "Tarifs Idara — logiciel de gestion pour daara et écoles coraniques";
            var metaDesc = Trim(StripTags(subtitle), 155);
            if (string.IsNullOrWhiteSpace(metaDesc))
                metaDesc = ar
                    ? "خطط اشتراك إدارة حسب حجم الدارة."
                    : "Les formules d'abonnement Idara, selon la taille de votre daara.";

            sb.Append("<title>").Append(E(metaTitle)).Append("</title>\n");
            sb.Append("<meta name=\"description\" content=\"").Append(E(metaDesc)).Append("\">\n");
            sb.Append("<link rel=\"canonical\" href=\"").Append(PageUrl).Append("\">\n");
            sb.Append("<link rel=\"icon\" href=\"").Append(SiteUrl).Append("/favicon.png\">\n");
            // Sans ces balises, le lien collé dans WhatsApp n'affiche qu'une URL nue.
            sb.Append("<meta property=\"og:type\" content=\"website\">\n");
            sb.Append("<meta property=\"og:site_name\" content=\"Idara\">\n");
            sb.Append("<meta property=\"og:title\" content=\"").Append(E(metaTitle)).Append("\">\n");
            sb.Append("<meta property=\"og:description\" content=\"").Append(E(metaDesc)).Append("\">\n");
            sb.Append("<meta property=\"og:url\" content=\"").Append(PageUrl).Append("\">\n");
            sb.Append("<meta property=\"og:image\" content=\"").Append(SiteUrl).Append("/og-image.png\">\n");
            sb.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">\n");
            sb.Append(Css());
            sb.Append("</head>\n<body>\n");

            // ---------- En-tête ----------
            sb.Append("<header class=\"top\">\n<div class=\"wrap topbar\">\n");
            sb.Append("<a class=\"brand\" href=\"").Append(SiteUrl).Append("\">idara</a>\n");
            sb.Append("<a class=\"lang\" href=\"/plans?lang=").Append(ar ? "fr" : "ar").Append("\">")
              .Append(ar ? "Français" : "العربية").Append("</a>\n");
            sb.Append("</div>\n</header>\n");

            sb.Append("<main class=\"wrap\">\n");
            sb.Append("<h1>").Append(E(title)).Append("</h1>\n");
            if (!string.IsNullOrWhiteSpace(subtitle))
                sb.Append("<p class=\"sub\">").Append(E(subtitle)).Append("</p>\n");
            if (!string.IsNullOrWhiteSpace(intro))
                sb.Append("<div class=\"intro\">").Append(Paragraphs(intro!)).Append("</div>\n");

            // ---------- Cartes ----------
            if (data.Plans.Count == 0)
            {
                sb.Append("<p class=\"empty\">")
                  .Append(ar ? "لا توجد خطة متاحة حاليا." : "Aucune formule n'est disponible pour le moment.")
                  .Append("</p>\n");
            }
            else
            {
                sb.Append("<section class=\"plans\">\n");
                foreach (var p in data.Plans) sb.Append(PlanCard(p, ar));
                sb.Append("</section>\n");
            }

            if (!string.IsNullOrWhiteSpace(footer))
                sb.Append("<p class=\"note\">").Append(E(footer)).Append("</p>\n");

            // ---------- FAQ ----------
            if (data.Faqs.Count > 0)
            {
                sb.Append("<section class=\"faq\">\n<h2>")
                  .Append(ar ? "أسئلة شائعة" : "Questions fréquentes").Append("</h2>\n");
                foreach (var f in data.Faqs)
                {
                    // <details> : repliable sans une ligne de JavaScript.
                    sb.Append("<details>\n<summary>")
                      .Append(E(Pick(f.Question, f.QuestionAr, ar))).Append("</summary>\n<div>")
                      .Append(Paragraphs(Pick(f.Answer, f.AnswerAr, ar) ?? string.Empty))
                      .Append("</div>\n</details>\n");
                }
                sb.Append("</section>\n");
            }

            // ---------- Contact ----------
            var wa = DigitsOnly(c.WhatsappNumber);
            if (!string.IsNullOrEmpty(wa) || !string.IsNullOrWhiteSpace(c.ContactEmail))
            {
                sb.Append("<section class=\"contact\">\n<h2>")
                  .Append(ar ? "تواصل معنا" : "Nous contacter").Append("</h2>\n<div class=\"actions\">\n");
                if (!string.IsNullOrEmpty(wa))
                {
                    sb.Append("<a class=\"btn wa\" href=\"https://wa.me/").Append(wa).Append("\">WhatsApp ")
                      .Append(E(c.WhatsappNumber!)).Append("</a>\n");
                }
                if (!string.IsNullOrWhiteSpace(c.ContactEmail))
                {
                    sb.Append("<a class=\"btn\" href=\"mailto:").Append(E(c.ContactEmail!)).Append("\">")
                      .Append(E(c.ContactEmail!)).Append("</a>\n");
                }
                sb.Append("</div>\n</section>\n");
            }

            sb.Append("</main>\n");
            sb.Append("<footer class=\"foot\"><div class=\"wrap\">Idara — <a href=\"")
              .Append(SiteUrl).Append("\">idara.sn</a></div></footer>\n");
            sb.Append("</body>\n</html>");
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        private static string PlanCard(SubscriptionPlanDto p, bool ar)
        {
            var sb = new StringBuilder(1500);
            sb.Append("<article class=\"plan").Append(p.IsHighlighted ? " hi" : "").Append("\">\n");
            if (p.IsHighlighted)
                sb.Append("<div class=\"badge\">").Append(ar ? "الأكثر اختيارا" : "Le plus choisi").Append("</div>\n");

            sb.Append("<h3>").Append(E(Pick(p.Name, p.NameAr, ar))).Append("</h3>\n");

            // La ligne d'accroche est TOUJOURS émise, même vide : sans elle, le
            // prix d'un plan sans accroche remonte et les cartes ne s'alignent
            // plus — c'est le premier chiffre que le lecteur compare.
            var tagline = Pick(p.Tagline, p.TaglineAr, ar);
            sb.Append("<p class=\"tag\">").Append(E(tagline)).Append("</p>\n");

            sb.Append("<p class=\"price\"><strong>").Append(Money(p.MonthlyPriceFcfa)).Append("</strong>")
              .Append("<span> FCFA / ").Append(ar ? "شهر" : "mois").Append("</span></p>\n");

            // Le tarif annuel n'est montré QUE s'il est réellement renseigné :
            // un « 0 FCFA / an » sur une page de tarifs discrédite tout le reste.
            if (p.AnnualPriceFcfa > 0)
            {
                sb.Append("<p class=\"annual\">").Append(Money(p.AnnualPriceFcfa)).Append(" FCFA / ")
                  .Append(ar ? "سنة" : "an");
                var saving = p.MonthlyPriceFcfa * 12 - p.AnnualPriceFcfa;
                if (saving > 0)
                    sb.Append(" <em>(").Append(ar ? "توفير " : "économie ").Append(Money(saving))
                      .Append(" FCFA)</em>");
                sb.Append("</p>\n");
            }

            // Les DEUX quotas, toujours : c'est la tranche d'élèves qui dit à un
            // directeur si la formule lui convient.
            sb.Append("<ul class=\"quotas\">\n<li>").Append(StudentRange(p, ar)).Append("</li>\n");
            sb.Append("<li>").Append(ar
                ? $"{Money(p.NotificationQuota)} إشعار في الشهر"
                : $"{Money(p.NotificationQuota)} notifications / mois").Append("</li>\n</ul>\n");

            if (p.Features.Count > 0)
            {
                sb.Append("<ul class=\"feats\">\n");
                foreach (var f in p.Features)
                {
                    sb.Append("<li class=\"").Append(f.Included ? "yes" : "no").Append("\">")
                      .Append(E(Pick(f.Label, f.LabelAr, ar))).Append("</li>\n");
                }
                sb.Append("</ul>\n");
            }

            sb.Append("</article>\n");
            return sb.ToString();
        }

        private static string StudentRange(SubscriptionPlanDto p, bool ar)
        {
            if (p.StudentMax == null && p.StudentMin == null)
                return ar ? "عدد التلاميذ غير محدود" : "Élèves illimités";
            if (p.StudentMax == null)
                return ar ? $"ابتداء من {p.StudentMin} تلميذ" : $"À partir de {p.StudentMin} élèves";
            var min = p.StudentMin ?? 1;
            return ar ? $"من {min} إلى {p.StudentMax} تلميذ" : $"De {min} à {p.StudentMax} élèves";
        }

        /// <summary>
        /// Version arabe si elle existe ET n'est pas vide, sinon le français.
        /// Une traduction manquante ne doit JAMAIS produire un trou dans la page.
        /// </summary>
        private static string? Pick(string? fr, string? arText, bool ar) =>
            ar && !string.IsNullOrWhiteSpace(arText) ? arText : fr;

        /// <summary>
        /// « 12 000 » avec un espace INSÉCABLE ordinaire (U+00A0) : un prix ne
        /// doit jamais se couper en fin de ligne. ⚠️ Volontairement PAS l'espace
        /// fine U+202F, plus élégante mais absente de certaines polices système
        /// de téléphones d'entrée de gamme — un carré au milieu d'un montant, sur
        /// une page tarifaire envoyée à un prospect, coûte cher.
        /// </summary>
        public static string Money(long v)
        {
            var s = Math.Abs(v).ToString();
            var sb = new StringBuilder();
            for (var i = 0; i < s.Length; i++)
            {
                if (i > 0 && (s.Length - i) % 3 == 0) sb.Append('\u00A0');
                sb.Append(s[i]);
            }
            return (v < 0 ? "-" : string.Empty) + sb;
        }

        /// <summary>
        /// Tout texte venant de la base est ÉCHAPPÉ. Le contenu est saisi au
        /// back-office, mais une page publique ne fait jamais confiance à ce
        /// qu'elle affiche.
        /// </summary>
        private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        /// <summary>Retours à la ligne saisis → paragraphes, contenu échappé.</summary>
        private static string Paragraphs(string s)
        {
            var parts = s.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length > 0) sb.Append("<p>").Append(E(t)).Append("</p>");
            }
            return sb.ToString();
        }

        private static string StripTags(string? s) => s ?? string.Empty;

        private static string Trim(string s, int max) =>
            s.Length <= max ? s : s[..max].TrimEnd() + "…";

        /// <summary>wa.me n'accepte que des chiffres (ni +, ni espace).</summary>
        private static string DigitsOnly(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in s) if (ch >= '0' && ch <= '9') sb.Append(ch);
            return sb.ToString();
        }

        private static string Css() => """
<style>
:root{--v:#0B744D;--vs:#E8F3EE;--t:#12211B;--t2:#5A6B63;--b:#E2E8E5;--bg:#F7F9F8}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--t);
  font-family:system-ui,-apple-system,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif;
  line-height:1.55;-webkit-text-size-adjust:100%}
.wrap{max-width:1080px;margin:0 auto;padding:0 20px}
.top{background:#fff;border-bottom:1px solid var(--b)}
.topbar{display:flex;align-items:center;justify-content:space-between;height:64px}
.brand{font-size:26px;font-weight:800;color:var(--v);text-decoration:none;letter-spacing:-.5px}
.lang{color:var(--v);text-decoration:none;font-weight:600;font-size:14px;
  border:1px solid var(--b);border-radius:999px;padding:6px 14px;background:#fff}
h1{font-size:34px;line-height:1.2;margin:40px 0 10px}
.sub{font-size:17px;color:var(--t2);margin:0 0 8px;max-width:680px}
.intro{color:var(--t2);max-width:680px}
.intro p{margin:8px 0}
.plans{display:grid;gap:18px;margin:32px 0 8px;
  grid-template-columns:repeat(auto-fit,minmax(240px,1fr))}
.plan{position:relative;background:#fff;border:1px solid var(--b);border-radius:16px;padding:22px}
.plan.hi{border-color:var(--v);box-shadow:0 6px 22px rgba(11,116,77,.12)}
.badge{position:absolute;top:-11px;inset-inline-start:22px;background:var(--v);color:#fff;
  font-size:11px;font-weight:700;padding:4px 10px;border-radius:999px;letter-spacing:.3px}
.plan h3{margin:4px 0 2px;font-size:19px}
.tag{margin:0 0 14px;color:var(--t2);font-size:13px;min-height:18px}
.price{margin:0}
.price strong{font-size:30px;color:var(--v);letter-spacing:-.5px}
.price span{color:var(--t2);font-size:14px}
.annual{margin:2px 0 14px;font-size:13px;color:var(--t2)}
.annual em{color:var(--v);font-style:normal;font-weight:600}
ul{list-style:none;padding:0;margin:0}
.quotas{border-top:1px solid var(--b);border-bottom:1px solid var(--b);padding:12px 0;margin:0 0 12px}
.quotas li{font-size:14px;font-weight:600;padding:3px 0}
.feats li{font-size:14px;padding:5px 0 5px 24px;color:var(--t)}
html[dir=rtl] .feats li{padding:5px 24px 5px 0}
.feats li{position:relative}
.feats li:before{position:absolute;inset-inline-start:0;font-weight:700}
.feats li.yes:before{content:"\2713";color:var(--v)}
.feats li.no{color:var(--t2);text-decoration:line-through}
.feats li.no:before{content:"\00D7";color:#B9C4BF}
.note{margin:18px 0 0;color:var(--t2);font-size:14px}
.empty{margin:40px 0;color:var(--t2)}
.faq{margin:44px 0 0}
.faq h2,.contact h2{font-size:22px;margin:0 0 14px}
details{background:#fff;border:1px solid var(--b);border-radius:12px;padding:14px 16px;margin-bottom:10px}
summary{cursor:pointer;font-weight:600;list-style:none;display:flex;justify-content:space-between;gap:12px}
summary::-webkit-details-marker{display:none}
summary:after{content:"+";color:var(--v);font-weight:800;flex:none}
details[open] summary:after{content:"\2212"}
details div p{color:var(--t2);margin:10px 0 0}
.contact{margin:44px 0 60px}
.actions{display:flex;flex-wrap:wrap;gap:12px}
.btn{display:inline-block;background:#fff;border:1px solid var(--b);border-radius:12px;
  padding:12px 18px;text-decoration:none;color:var(--t);font-weight:600}
.btn.wa{background:var(--v);border-color:var(--v);color:#fff}
.foot{border-top:1px solid var(--b);background:#fff;padding:22px 0;color:var(--t2);font-size:14px}
.foot a{color:var(--v)}
@media(max-width:600px){h1{font-size:27px;margin-top:28px}.price strong{font-size:26px}}
@media print{
  .top,.foot,.contact,.lang{display:none}
  body{background:#fff}
  .plan{break-inside:avoid;border-color:#999}
  details{break-inside:avoid}
  details div{display:block!important}
}
</style>
""";
    }
}
