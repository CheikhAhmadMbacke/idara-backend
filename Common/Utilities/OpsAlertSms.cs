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
        /// <para><b>La longueur est MESURÉE, pas estimée</b> (§192). Le nom d'un
        /// daara réel fait jusqu'à 45 caractères (§280) : additionner des
        /// longueurs supposées ferait déborder sur un deuxième segment sans que
        /// rien ne le signale, puisque le message s'afficherait normalement.
        /// C'est la phrase qui est rognée, jamais la date ni le préfixe — sans
        /// l'heure, l'alerte ne dit plus quand.</para>
        /// </summary>
        /// <param name="headline">La phrase, sans préfixe ni date.</param>
        /// <param name="whenUtc">Instant de l'événement.</param>
        public static string Compose(string headline, DateTime whenUtc)
        {
            var stamp = whenUtc.ToString("dd/MM HH'h'mm",
                System.Globalization.CultureInfo.InvariantCulture);

            // Assaini AVANT de mesurer : « é » compte pour un caractère GSM-7,
            // « ë » n'existe pas dans l'alphabet et basculerait tout le message
            // en UCS-2 — 70 caractères par segment au lieu de 160 (§225).
            var body = Gsm7Text.Sanitize(headline ?? string.Empty).Trim();
            if (body.EndsWith('.')) body = body[..^1];

            var overhead = Prefix.Length + 2 + stamp.Length; // ". " avant la date
            var room = SingleSegmentLength - overhead;
            if (room < 1) return Prefix + stamp;             // inatteignable, mais jamais de longueur négative

            if (body.Length > room)
                body = body[..Math.Max(0, room - 1)].TrimEnd() + "~";

            return $"{Prefix}{body}. {stamp}";
        }

        /// <summary>
        /// Raccourcit un nom d'établissement pour qu'il tienne dans une phrase
        /// d'alerte.
        ///
        /// <para>Un NOM se coupe, il ne se réduit pas (§243) : on ne peut pas
        /// abréger « Daara Serigne Fallou Mbacké » sans écrire quelque chose de
        /// faux. Le tilde final dit que la lecture continue ailleurs.</para>
        /// </summary>
        public static string ShortName(string? name, int max = 40)
        {
            var n = Gsm7Text.Sanitize(name ?? string.Empty).Trim();
            if (n.Length == 0) return "(sans nom)";
            return n.Length <= max ? n : n[..(max - 1)].TrimEnd() + "~";
        }
    }
}
