using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Requête côté client (Guardian via Flutter) pour initier un paiement parent → école.
    ///
    /// Flow unique côté SenePay (confirmé par le dev SenePay le 2026-05-24, malgré
    /// que la doc publique §3 décrive un faux 2-step) :
    /// - **Wave** : { studentId, invoiceId? ou amount, operator="wave", customerPhone } → redirectUrl Wave
    /// - **Orange Money** : MÊME shape PLUS `otpCode` (6 chiffres, généré par le
    ///   parent via `#144#391#` AVANT de cliquer "Payer"). SenePay rejette le
    ///   1er appel sans otpCode pour Orange avec `400 — Code OTP invalide`.
    ///
    /// Un seul appel suffit dans tous les cas — pas de 2-step OTP.
    /// </summary>
    public class InitiatePaymentRequestDto
    {
        /// <summary>Élève concerné par le paiement.</summary>
        [Required]
        public int? StudentId { get; set; }

        /// <summary>Si renseigné : règle une Invoice mensuelle (mode FixedAmount). Sinon : paiement libre (mode FreeAmount).</summary>
        public int? InvoiceId { get; set; }

        /// <summary>Montant FCFA voulu par le parent. Ignoré si InvoiceId — sinon obligatoire ≥ 200.</summary>
        public long? Amount { get; set; }

        /// <summary>
        /// DÉPRÉCIÉ (2026-07-07) — ignoré. Le paiement est désormais Wave
        /// uniquement, forcé côté serveur. Champ gardé nullable pour la
        /// rétro-compatibilité d'anciennes APK.
        /// </summary>
        public string? Operator { get; set; }

        /// <summary>
        /// DÉPRÉCIÉ (2026-07-07) — ignoré. Le numéro du payeur est désormais
        /// récupéré en base (identité par téléphone). Gardé nullable pour la
        /// rétro-compatibilité d'anciennes APK.
        /// </summary>
        public string? CustomerPhone { get; set; }

        /// <summary>DÉPRÉCIÉ (2026-07-07) — ignoré (plus d'Orange, donc plus d'OTP).</summary>
        public string? OtpCode { get; set; }
    }
}
