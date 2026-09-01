namespace Idara.API.Common.Utilities
{
    /// <summary>Ce qui a réellement empêché le décaissement.</summary>
    public enum PayoutFailureCause
    {
        /// <summary>Le prestataire ne peut pas payer : réserve à sec, opérateur
        /// indisponible, passerelle en erreur. <b>Rien à corriger côté école</b> —
        /// c'est à nous d'agir, et vite.</summary>
        ProviderOutage = 0,

        /// <summary>Numéro, opérateur ou nom du bénéficiaire refusés. L'école
        /// corrige elle-même et réessaie.</summary>
        BadRecipient = 1,

        /// <summary>Refus de l'opérateur mobile (compte fermé, plafond du
        /// bénéficiaire, KYC). Ni nous ni l'école ne pouvons le lever.</summary>
        OperatorRefusal = 2,

        /// <summary>Motif non reconnu — à lire à la main.</summary>
        Unknown = 3,
    }

    /// <summary>
    /// Classe le motif brut renvoyé par le prestataire.
    ///
    /// <para><b>Pourquoi classer plutôt que recopier.</b> Un motif brut
    /// (« PAYOUT_FAILED: insufficient balance on merchant reserve ») ne dit pas
    /// ce qu'il faut FAIRE, et il induit même en erreur : le message affiché à
    /// l'école accusait ses coordonnées alors que la réserve du prestataire était
    /// à sec (§111). Or la réaction attendue n'est pas la même — recharger la
    /// réserve, corriger un numéro, ou ne rien pouvoir faire. C'est le classement
    /// qui décide de l'urgence de l'alerte et du conseil qui l'accompagne.</para>
    ///
    /// <para><b>Source unique</b> : le même classement sert au message rendu à
    /// l'école et à l'e-mail d'alerte. Deux copies finiraient par se contredire,
    /// et l'école lirait « coordonnées invalides » pendant que l'e-mail dirait
    /// « réserve à sec ».</para>
    ///
    /// <para>Pure et statique exprès (§133) : un classement qu'on ne pourrait
    /// vérifier qu'en provoquant un vrai échec de décaissement ne se vérifie
    /// jamais.</para>
    /// </summary>
    public static class PayoutFailureClassifier
    {
        public static PayoutFailureCause Classify(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return PayoutFailureCause.Unknown;
            var r = reason;

            bool Has(string needle) => r.Contains(needle, StringComparison.OrdinalIgnoreCase);

            // Testé EN PREMIER : « insufficient balance » revient du prestataire
            // alors que le solde de l'ÉCOLE a déjà été validé sous verrou en
            // amont — c'est donc toujours la réserve du prestataire qui manque.
            if (Has("insufficient") || Has("balance") || Has("unavailable")
                || Has("indisponible") || Has("502") || Has("503") || Has("504")
                || Has("timeout") || Has("gateway"))
                return PayoutFailureCause.ProviderOutage;

            if (Has("invalid") || Has("phone") || Has("recipient") || Has("msisdn")
                || Has("operator not supported") || Has("reference_id"))
                return PayoutFailureCause.BadRecipient;

            if (Has("refus") || Has("declined") || Has("rejected") || Has("kyc")
                || Has("limit") || Has("blocked") || Has("closed"))
                return PayoutFailureCause.OperatorRefusal;

            return PayoutFailureCause.Unknown;
        }

        /// <summary>
        /// Vrai si l'échec vient du PRESTATAIRE et non des coordonnées saisies.
        /// Conserve le contrat de l'ancien helper privé du contrôleur : c'est
        /// lui qui décide si l'on accuse l'école ou si l'on dit honnêtement
        /// qu'on ne peut pas payer en ce moment (§111).
        /// </summary>
        public static bool IsTemporaryOutage(string? reason) =>
            Classify(reason) == PayoutFailureCause.ProviderOutage;

        public static string Label(PayoutFailureCause cause) => cause switch
        {
            PayoutFailureCause.ProviderOutage => "Le prestataire ne peut pas decaisser",
            PayoutFailureCause.BadRecipient => "Coordonnees du beneficiaire refusees",
            PayoutFailureCause.OperatorRefusal => "Refus de l'operateur mobile",
            _ => "Motif non reconnu",
        };

        /// <summary>Ce qu'il y a à faire, formulé comme une action et non comme
        /// un constat.</summary>
        public static string Advice(PayoutFailureCause cause) => cause switch
        {
            PayoutFailureCause.ProviderOutage =>
                "L'ecole n'y est pour rien et son solde a ete restitue. Verifier la reserve de "
                + "decaissement chez le prestataire et la recharger, puis prevenir l'ecole qu'elle "
                + "peut reessayer. Tant que la reserve est vide, TOUS les retraits echoueront.",
            PayoutFailureCause.BadRecipient =>
                "L'ecole peut corriger elle-meme : numero, operateur ou nom du beneficiaire. "
                + "Son solde a ete restitue. Un appel suffit generalement.",
            PayoutFailureCause.OperatorRefusal =>
                "Le compte mobile du beneficiaire refuse le versement (plafond, compte ferme, "
                + "identification incomplete). A regler par le beneficiaire aupres de son operateur ; "
                + "le solde de l'ecole a ete restitue.",
            _ =>
                "Motif non reconnu : lire le detail ci-dessus et, si le cas se repete, l'ajouter au "
                + "classement pour que la prochaine alerte sache quoi conseiller.",
        };
    }
}
