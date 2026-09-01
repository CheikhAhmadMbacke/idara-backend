using System.Text.RegularExpressions;

namespace Idara.API.Common.Utilities
{
    /// <summary>Réseau du destinataire — c'est lui qui décide du prix unitaire.</summary>
    public enum SmsNetwork
    {
        /// <summary>Orange Sénégal (77, 78) — tarif on-net, le moins cher.</summary>
        OnNet = 0,

        /// <summary>Autre opérateur sénégalais (70 Expresso, 75 Promobile, 76 Free) — off-net.</summary>
        OffNet = 1,

        /// <summary>Hors Sénégal. ~11× le tarif on-net : c'est le carburant de la
        /// fraude au SMS pumping. Idara ne doit JAMAIS en envoyer (cf.
        /// <see cref="SenegalPhone.Normalize"/>), la valeur n'existe ici que pour
        /// qu'une ligne aberrante du registre se voie au lieu d'être silencieuse.</summary>
        International = 2,
    }

    /// <summary>Encodage réellement utilisé sur le réseau, donc taille du segment.</summary>
    public enum SmsEncoding
    {
        /// <summary>GSM 03.38 sur 7 bits : 160 caractères seuls, 153 par segment concaténé.</summary>
        Gsm7 = 0,

        /// <summary>UCS-2 (dès qu'UN caractère sort du GSM-7, l'arabe par exemple) :
        /// 70 caractères seuls, 67 par segment concaténé.</summary>
        Ucs2 = 1,
    }

    /// <summary>Découpage facturable d'un message.</summary>
    /// <param name="Encoding">Encodage imposé par le contenu.</param>
    /// <param name="CharCount">Longueur en unités facturables (un caractère de
    /// l'extension GSM-7 en vaut deux).</param>
    /// <param name="Segments">Segments selon la norme SMS — l'hypothèse de référence.</param>
    /// <param name="SegmentsFixed160">Segments selon l'autre lecture possible du
    /// contrat Orange (« facturation par lot de 160 caractères »), qui ignorerait
    /// l'encodage. Voir <see cref="SmsSegmentCalculator"/>.</param>
    public record SmsSegmentation(
        SmsEncoding Encoding,
        int CharCount,
        int Segments,
        int SegmentsFixed160);

    /// <summary>
    /// Calcule ce qu'un SMS coûtera RÉELLEMENT : son encodage, son nombre de
    /// segments facturés, le réseau du destinataire et donc son prix.
    ///
    /// <para><b>Pourquoi le segment et pas le message.</b> Orange facture le
    /// segment, pas l'envoi. Un même message « part une fois » et peut être
    /// facturé quatre fois : mesuré sur les vrais gabarits d'Idara, un rappel de
    /// mensualité pèse 1 segment en français seul, 2 en arabe seul, et
    /// <b>4 en bilingue</b> — l'arabe fait basculer tout le corps en UCS-2, où le
    /// segment ne vaut plus que 70 caractères (§88). Compter les messages
    /// donnerait un total quatre fois trop bas face à la facture Sonatel.</para>
    ///
    /// <para><b>Les deux hypothèses de facturation.</b> La grille Orange dit
    /// « facturation par lot de 160 caractères », ce qui est <b>ambigu en
    /// UCS-2</b> (physiquement 70 caractères par segment). Tant qu'une vraie
    /// facture n'a pas tranché, on calcule les <b>deux</b> lectures et on les
    /// affiche côte à côte au back-office : c'est la comparaison avec la facture
    /// de Sonatel qui dira laquelle retenir. Bâtir sur une seule hypothèse, c'est
    /// se condamner à ne jamais savoir laquelle était la bonne.</para>
    ///
    /// <para>Pure et statique <b>exprès</b> (motif §133) : un calcul de coût qu'on
    /// ne pourrait vérifier qu'en payant de vrais SMS ne serait jamais vérifié.</para>
    /// </summary>
    public static class SmsSegmentCalculator
    {
        // Alphabet GSM 03.38 « basique ». Tout caractère absent d'ici et de
        // l'extension fait basculer le message ENTIER en UCS-2.
        private const string Gsm7Basic =
            "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?"
            + "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

        // Caractères de l'extension : présents en GSM-7 mais comptés DOUBLE
        // (ils sont précédés d'un échappement sur le réseau).
        private const string Gsm7Extension = "^{}\\[~]|€";

        // Longueurs de segment normalisées. Les valeurs « concaténées » sont plus
        // courtes : un en-tête (UDH) mange de la place dans chaque segment.
        private const int Gsm7Single = 160, Gsm7Concat = 153;
        private const int Ucs2Single = 70, Ucs2Concat = 67;

        /// <summary>Lot du contrat Orange, dans sa lecture littérale.</summary>
        private const int FixedLotSize = 160;

        /// <summary>Mobile sénégalais en E.164 — la SEULE forme qu'Idara envoie.</summary>
        private static readonly Regex SenegalE164 = new(@"^\+221[0-9]{9}$", RegexOptions.Compiled);

        /// <summary>
        /// Découpe un message. Ne lève jamais : un message vide vaut 0 segment
        /// (un envoi vide est refusé bien plus haut, mais le registre doit
        /// pouvoir enregistrer n'importe quoi sans casser).
        /// </summary>
        public static SmsSegmentation Measure(string? message)
        {
            if (string.IsNullOrEmpty(message))
                return new SmsSegmentation(SmsEncoding.Gsm7, 0, 0, 0);

            var isGsm7 = true;
            var units = 0;
            foreach (var c in message)
            {
                if (Gsm7Basic.IndexOf(c) >= 0) { units++; continue; }
                if (Gsm7Extension.IndexOf(c) >= 0) { units += 2; continue; }
                isGsm7 = false;
                break;
            }

            // Dès qu'un seul caractère sort de l'alphabet, TOUT le message bascule
            // en UCS-2 et se compte alors en caractères réels, pas en unités GSM.
            if (!isGsm7) units = message.Length;

            var (single, concat) = isGsm7 ? (Gsm7Single, Gsm7Concat) : (Ucs2Single, Ucs2Concat);
            var segments = units <= single ? 1 : CeilDiv(units, concat);

            return new SmsSegmentation(
                isGsm7 ? SmsEncoding.Gsm7 : SmsEncoding.Ucs2,
                units,
                segments,
                CeilDiv(message.Length, FixedLotSize));
        }

        /// <summary>
        /// Réseau d'un numéro déjà normalisé en E.164. Tout ce qui n'est pas un
        /// mobile sénégalais est <see cref="SmsNetwork.International"/> — y
        /// compris une saisie aberrante : mieux vaut une ligne visiblement chère
        /// dans le registre qu'un coût rangé au tarif local par défaut.
        /// </summary>
        public static SmsNetwork NetworkOf(string? phoneE164)
        {
            if (string.IsNullOrWhiteSpace(phoneE164)) return SmsNetwork.International;
            var p = phoneE164.Trim();
            if (!SenegalE164.IsMatch(p)) return SmsNetwork.International;

            // +221 7X XXX XX XX → le préfixe à deux chiffres donne l'opérateur.
            var prefix = p.Substring(4, 2);
            return prefix is "77" or "78" ? SmsNetwork.OnNet : SmsNetwork.OffNet;
        }

        /// <summary>Vrai si le numéro est un mobile sénégalais en E.164 — la seule
        /// destination qu'Idara s'autorise.</summary>
        public static bool IsSenegalMobileE164(string? phoneE164) =>
            !string.IsNullOrWhiteSpace(phoneE164) && SenegalE164.IsMatch(phoneE164.Trim());

        /// <summary>
        /// Coût estimé en <b>centimes</b> de FCFA. Les centimes et non les francs
        /// parce que le tarif on-net vaut 3,5 F : arrondir chaque SMS au franc
        /// ferait dériver le total de 14 % sur des milliers de lignes, et c'est
        /// précisément ce total qu'on veut confronter à la facture Sonatel.
        /// </summary>
        public static long CostCentimes(int segments, SmsNetwork network,
            long onNetCentimes, long offNetCentimes, long internationalCentimes) =>
            segments * network switch
            {
                SmsNetwork.OnNet => onNetCentimes,
                SmsNetwork.OffNet => offNetCentimes,
                _ => internationalCentimes,
            };

        private static int CeilDiv(int value, int divisor) =>
            divisor <= 0 ? 0 : (value + divisor - 1) / divisor;
    }
}
