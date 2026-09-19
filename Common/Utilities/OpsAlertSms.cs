using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Ce qui mérite de faire sonner TON téléphone, et comment le dire en un
    /// seul segment.
    ///
    /// <para><b>Le SMS est une sonnette, pas un rapport.</b> Il te dit qu'il se
    /// passe quelque chose et lequel ; le détail (montant, bénéficiaire,
    /// référence, conseil) reste dans l'e-mail et sur l'écran du back-office.
    /// C'est pour ça qu'il n'y a ici qu'une phrase : un SMS qu'on ne peut pas
    /// lire d'un coup d'œil sur l'écran verrouillé ne sert à rien de plus que
    /// l'e-mail qu'il double.</para>
    ///
    /// <para><b>Pourquoi une liste BLANCHE et non une liste noire.</b> Un
    /// <see cref="OpsAlertKind"/> ajouté demain ne doit pas se mettre à sonner
    /// tout seul. La règle produit posée le 2026-09-19 est étroite — nouvelle
    /// inscription, et panne imputable à NOUS ou au PRESTATAIRE — et un défaut
    /// imputable à l'école (numéro de bénéficiaire faux, refus de son opérateur)
    /// n'appelle aucune réaction urgente de ta part : il est déjà dans l'e-mail.
    /// Ouvrir le SMS à tout serait le meilleur moyen de ne plus le lire.</para>
    ///
    /// <para>Pure et statique exprès (§133) : une règle qu'on ne pourrait
    /// vérifier qu'en provoquant une vraie panne de décaissement ne se vérifie
    /// jamais.</para>
    /// </summary>
    public static class OpsAlertSms
    {
        /// <summary>
        /// Gabarit porté par le registre des notifications. Le préfixe
        /// <c>OPS_</c> n'est pas décoratif : c'est lui que le garde-fou compte
        /// pour la bourse quotidienne du canal d'alerte, et lui qu'on filtre
        /// pour relire ce qui t'a été envoyé.
        /// </summary>
        public const string TemplateCode = "OPS_ALERT";

        /// <summary>
        /// Longueur maximale d'un SMS à un segment en alphabet GSM-7. Au-delà,
        /// l'opérateur facture DEUX segments pour un message qui n'apporte rien
        /// de plus (§192).
        /// </summary>
        public const int SingleSegmentLength = 160;

        /// <summary>Marque d'expéditeur, pour qu'il se reconnaisse sans être ouvert.</summary>
        private const string Prefix = "Idara: ";

        /// <summary>
        /// Vrai si cette nature d'événement doit faire partir un SMS.
        ///
        /// <para>Trois familles, et rien d'autre :</para>
        /// <list type="bullet">
        ///   <item>l'argent d'une école est bloqué par une panne qui vient de
        ///   nous ou du prestataire — c'est TOI qui dois agir, et vite ;</item>
        ///   <item>un directeur entre dans la plateforme — c'est le moment où un
        ///   appel convertit, et il se périme en quelques heures ;</item>
        ///   <item>le canal SMS lui-même s'est fermé — sans ce message, la seule
        ///   façon de l'apprendre serait de constater que plus rien ne part.</item>
        /// </list>
        ///
        /// <para>🔴 Délibérément ABSENTS : <see cref="OpsAlertKind.WithdrawalFailed"/>
        /// (numéro du bénéficiaire refusé, refus de son opérateur — l'école
        /// corrige seule, cf. <see cref="PayoutFailureClassifier"/>) et
        /// <see cref="OpsAlertKind.SmsSchoolRunaway"/> (une école qui dépasse son
        /// plafond est isolée toute seule, et il y en a eu 14 en une journée le
        /// 2026-09-16 : en SMS, cette seule nature aurait épuisé la bourse).</para>
        /// </summary>
        public static bool ShouldSend(OpsAlertKind kind) => kind switch
        {
            // --- L'argent est bloqué, et ce n'est pas la faute de l'école ---
            OpsAlertKind.WithdrawalProviderOutage => true,
            OpsAlertKind.WithdrawalStuck => true,
            OpsAlertKind.PayoutAnomaly => true,
            OpsAlertKind.PayinProviderRejected => true,

            // --- Un directeur arrive ---
            OpsAlertKind.SchoolAccountCreated => true,
            OpsAlertKind.SchoolKycSubmitted => true,

            // --- Le canal d'envoi s'est fermé de lui-même ---
            // Le seul cas où le SMS parle du SMS. Il passe malgré le palier
            // atteint (voir SmsBudgetGuard) : couper précisément le message qui
            // annonce la coupure laisserait le silence comme unique symptôme.
            OpsAlertKind.SmsHardCapReached => true,
            OpsAlertKind.SmsForeignRecipientBlocked => true,
            OpsAlertKind.AuthCodeChannelClosed => true,

            _ => false,
        };

        /// <summary>
        /// Compose le message final : <c>Idara: &lt;phrase&gt;. &lt;jour&gt; &lt;heure&gt;</c>.
        ///
        /// <para><b>L'horodatage est en UTC et c'est volontaire</b> : le Sénégal
        /// est à UTC+0 toute l'année (pas d'heure d'été), donc l'heure UTC EST
        /// l'heure de Dakar. Convertir vers « l'heure locale du serveur »
        /// donnerait le même résultat aujourd'hui et un résultat faux le jour où
        /// le VPS change de fuseau.</para>
        ///
        /// <para>🔴 <b>La longueur est MESURÉE par le calculateur de segments, pas
        /// comptée en caractères.</b> Les deux ne sont pas la même chose : dans
        /// l'alphabet GSM-7, <c>~ [ ] { } \ | ^ €</c> vivent dans une table
        /// d'extension et coûtent <b>DEUX</b> caractères chacun. Un premier
        /// réglage bornait à 160 caractères et produisait bel et bien des messages
        /// à <b>deux segments</b> — à cause du <c>~</c> qui marquait justement la
        /// troncature. Estimer une longueur ici, c'est refaire l'erreur du §192 :
        /// on rogne donc en boucle jusqu'à ce que la MESURE dise un segment.</para>
        ///
        /// <para>C'est la phrase qui est rognée, jamais la date ni le préfixe —
        /// sans l'heure, l'alerte ne dit plus quand.</para>
        /// </summary>
        /// <param name="headline">La phrase, sans préfixe ni date.</param>
        /// <param name="whenUtc">Instant de l'événement.</param>
        public static string Compose(string headline, DateTime whenUtc)
        {
            var stamp = whenUtc.ToString("dd/MM HH'h'mm",
                System.Globalization.CultureInfo.InvariantCulture);

            var body = Clean(headline);
            if (body.EndsWith('.')) body = body[..^1];

            string Rendu(string corps) => corps.Length == 0
                ? $"{Prefix}{stamp}"
                : $"{Prefix}{corps}. {stamp}";

            var text = Rendu(body);

            // Rognage MESURÉ. La boucle décroît strictement et s'arrête au pire sur
            // un corps vide, donc elle termine toujours — même si l'appelant passe
            // une phrase entièrement composée de caractères à double coût.
            while (body.Length > 0 && SmsSegmentCalculator.Measure(text).Segments > 1)
            {
                // On retire par petits paquets : un caractère à la fois ferait
                // jusqu'à 160 mesures pour rien.
                var coupe = Math.Max(1, body.Length / 16);
                body = body[..(body.Length - coupe)].TrimEnd();
                text = Rendu(body);
            }

            return text;
        }

        /// <summary>
        /// Marque de troncature.
        ///
        /// <para>Un point et non <c>~</c> ni <c>…</c> : le premier coûte DEUX
        /// caractères GSM-7 (table d'extension) et le second n'appartient pas du
        /// tout à l'alphabet, donc il ferait basculer le message entier en UCS-2
        /// et <b>doublerait la facture</b> (§225). Le point, lui, se lit comme une
        /// abréviation et coûte ce qu'il montre.</para>
        /// </summary>
        private const string TruncationMark = ".";

        /// <summary>
        /// Assainit un fragment destiné au SMS : alphabet GSM-7, et <b>une seule
        /// ligne</b>.
        ///
        /// <para>🔴 Les sauts de ligne sont écrasés, et ce n'est pas cosmétique.
        /// <c>LF</c> appartient à l'alphabet GSM-7, donc il traversait
        /// l'assainissement intact — or une partie de ce message vient de texte
        /// SAISI PAR UNE ÉCOLE (le nom du daara). Un nom contenant
        /// « Daara X ⏎⏎ Idara: retrait bloque URGENT appelez 77… » aurait produit
        /// un SMS qui ressemble à <b>deux</b> alertes, dont une fabriquée. Un
        /// canal d'alerte doit être le seul à pouvoir écrire ses propres
        /// phrases.</para>
        /// </summary>
        private static string Clean(string? input)
        {
            var t = Gsm7Text.Sanitize(input ?? string.Empty);

            var sb = new System.Text.StringBuilder(t.Length);
            var espacePrecedente = false;
            foreach (var c in t)
            {
                var estEspace = char.IsWhiteSpace(c) || char.IsControl(c);
                if (estEspace)
                {
                    if (!espacePrecedente && sb.Length > 0) sb.Append(' ');
                    espacePrecedente = true;
                    continue;
                }
                sb.Append(c);
                espacePrecedente = false;
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Raccourcit un nom d'établissement pour qu'il tienne dans une phrase
        /// d'alerte.
        ///
        /// <para>Un NOM se coupe, il ne se réduit pas (§243) : on ne peut pas
        /// abréger « Daara Serigne Fallou Mbacké » sans écrire quelque chose de
        /// faux. Le point final dit que la lecture continue ailleurs.</para>
        ///
        /// <para>Ce n'est qu'un pré-rognage de confort : c'est
        /// <see cref="Compose"/> qui garantit le segment unique, en mesurant.</para>
        /// </summary>
        public static string ShortName(string? name, int max = 40)
        {
            var n = Clean(name);
            if (n.Length == 0) return "(sans nom)";
            return n.Length <= max ? n : n[..(max - 1)].TrimEnd() + TruncationMark;
        }
    }
}
