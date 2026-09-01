namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Traduit un code de gabarit en événement lisible. <b>Source unique</b> :
    /// l'écran, l'export et l'e-mail d'alerte disent tous la même chose d'un même
    /// envoi — un tableau où la moitié des lignes affiche <c>INVOICE_DUE</c> et
    /// l'autre « Mensualité due » oblige à traduire de tête pour additionner.
    ///
    /// <para>Un code inconnu est renvoyé TEL QUEL plutôt que masqué : mieux vaut
    /// un libellé technique qu'une ligne « Autre » qui perdrait l'information le
    /// jour où un gabarit est ajouté sans passer ici.</para>
    /// </summary>
    public static class SmsEventLabels
    {
        public static string Of(string? templateCode) => templateCode switch
        {
            "INVOICE_DUE" => "Mensualite due",
            "INVOICE_DUE_SOON" => "Rappel avant echeance",
            "INVOICE_OVERDUE" => "Rappel de retard",
            "REGISTRATION_DUE" => "Frais d'inscription dus",
            "REGISTRATION_OVERDUE" => "Frais d'inscription en retard",
            "FREE_PAYMENT_DUE" => "Rappel mensuel (montant libre)",
            "PAYMENT_RECEIVED" => "Paiement recu",
            "CASH_PAYMENT_CANCELLED" => "Encaissement annule",
            "CREDENTIALS_SMS" => "Identifiants de connexion",
            "OTP" => "Code de connexion",
            "TRANSFER_RECEIVED" => "Transfert recu",
            "DONATION_THANKS" => "Remerciement de don",
            "DONATION_RECEIVED_SCHOOL" => "Don recu (ecole)",
            "SUBSCRIPTION_DUE_SOON" => "Abonnement a echeance",
            "SUBSCRIPTION_CHARGED" => "Abonnement preleve",
            "SUBSCRIPTION_AUTO_UPGRADE" => "Changement de palier",
            "WALLET_ADJUSTED" => "Solde ajuste",
            "SCHOOL_PAYMENT_RECEIVED" => "Paiement recu (ecole)",
            "BROADCAST" => "Message de la plateforme",
            null or "" => "Inconnu",
            _ => templateCode,
        };

        /// <summary>Réseau en clair, avec ce qui explique le prix.</summary>
        public static string NetworkLabel(SmsNetwork network) => network switch
        {
            SmsNetwork.OnNet => "Orange (on-net)",
            SmsNetwork.OffNet => "Autre operateur (off-net)",
            _ => "International",
        };

        public static string EncodingLabel(SmsEncoding encoding) =>
            encoding == SmsEncoding.Ucs2 ? "UCS-2 (70 car./segment)" : "GSM-7 (160 car./segment)";
    }
}
