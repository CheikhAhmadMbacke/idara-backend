namespace Idara.API.Enums
{
    /// <summary>
    /// Nature métier d'un <see cref="Models.Payment"/>. Remplace la détection
    /// fragile par champs null (StudentId/GuardianId) qui ne distinguait pas un
    /// topup d'un don (les deux ont Student/Guardian null). Le webhook payin
    /// branche désormais dessus (crédit invoice, notif, reçu, poches wallet).
    /// - SchoolFee : paiement parent → école (mensualité ou montant libre).
    /// - WalletTopup : recharge du wallet par le SchoolAdmin (Phase 4).
    /// - Donation : don d'un DONATEUR → école (StudentId/GuardianId null,
    ///   DonorId renseigné). Crédite la poche « Don » du wallet école.
    /// - OcrPages : l'école achète des pages de lecture de cahier. 🔴 Ne crédite
    ///   PAS le wallet école : l'argent est un revenu de la plateforme, pas une
    ///   somme due à l'école. Il entre donc dans P (§112), et le webhook octroie
    ///   les pages au lieu de créditer un solde. ⚠️ Ce fut longtemps la SEULE
    ///   nature dans ce cas ; depuis le 2026-09-19, Subscription la rejoint.
    /// - Subscription : l'école règle son ABONNEMENT depuis son lien public,
    ///   parce que son wallet était vide au jour du prélèvement. 🔴 Comme
    ///   OcrPages, ne crédite AUCUN wallet : c'est un revenu de la plateforme.
    ///   Le webhook solde la facture d'abonnement, avance le cycle et rend
    ///   l'accès — au lieu de créditer un solde.
    /// - AssistantCredits : l'école achète des commandes de l'assistant IA
    ///   (2026-10-07). 🔴 Même règle qu'OcrPages, et compté avec lui partout
    ///   (<see cref="PaymentPurposes.IsPlatformService"/>) : aucun wallet
    ///   crédité, revenu de la plateforme, le webhook octroie des commandes.
    /// </summary>
    public enum PaymentPurpose
    {
        SchoolFee = 0,
        WalletTopup = 1,
        Donation = 2,
        OcrPages = 3,
        Subscription = 4,
        AssistantCredits = 5
    }

    /// <summary>
    /// 🔑 Les SERVICES que la plateforme vend aux écoles (lecture de cahier,
    /// assistant IA). Un seul endroit pour les nommer : la finance (P, GMV,
    /// marge) doit les traiter à l'identique, et un objet oublié dans un seul
    /// filtre compterait la recette deux fois sans que rien ne le signale.
    /// </summary>
    public static class PaymentPurposes
    {
        public static bool IsPlatformService(PaymentPurpose p) =>
            p == PaymentPurpose.OcrPages || p == PaymentPurpose.AssistantCredits;

        /// <summary>
        /// L'argent va d'une école À LA PLATEFORME (services ET abonnement payé
        /// par Wave). Rien de tout cela n'est du volume d'affaires des familles :
        /// le compter dans le GMV gonflerait le produit de notre propre chiffre.
        /// </summary>
        public static bool PaysPlatform(PaymentPurpose p) =>
            IsPlatformService(p) || p == PaymentPurpose.Subscription;
    }
}
