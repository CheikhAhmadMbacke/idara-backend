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

        /// <summary>"wave" ou "orange" — seuls opérateurs supportés MVP Sénégal.</summary>
        [Required]
        [RegularExpression("^(wave|orange)$", ErrorMessage = "Operator must be 'wave' or 'orange'")]
        public string Operator { get; set; } = string.Empty;

        /// <summary>Numéro national 9 chiffres commençant par 7 (ex: "771234567"). On préfixe "+221" côté serveur.</summary>
        [Required]
        [RegularExpression(@"^7\d{8}$", ErrorMessage = "CustomerPhone must be a 9-digit Senegal mobile number starting with 7")]
        public string CustomerPhone { get; set; } = string.Empty;

        /// <summary>
        /// OTP Orange Money — 6 chiffres, REQUIS pour operator="orange",
        /// IGNORÉ pour operator="wave". Le parent génère l'OTP côté
        /// téléphone via le code USSD opérateur (ex: #144#391# pour Orange SN)
        /// AVANT d'appeler cet endpoint.
        /// </summary>
        public string? OtpCode { get; set; }
    }
}
