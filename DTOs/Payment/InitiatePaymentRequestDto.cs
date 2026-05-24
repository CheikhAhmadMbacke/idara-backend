using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Requête côté client (Guardian via Flutter) pour initier un paiement parent → école.
    ///
    /// Cas d'usage :
    /// - 1er appel Wave : { studentId, invoiceId? ou amount, operator="wave", customerPhone }
    /// - 1er appel Orange : { studentId, invoiceId? ou amount, operator="orange", customerPhone }
    ///   → SenePay répond OTP_REQUIRED → le client demande le code à l'utilisateur
    /// - 2e appel Orange : { paymentId, otpCode, customerPhone } — réutilise le même Payment.Id
    /// </summary>
    public class InitiatePaymentRequestDto
    {
        /// <summary>Présent UNIQUEMENT pour le 2e appel Orange (avec OTP). Absent pour le 1er appel.</summary>
        public int? PaymentId { get; set; }

        /// <summary>Élève concerné par le paiement. Requis pour le 1er appel ; ignoré sur le 2e appel OTP.</summary>
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

        /// <summary>OTP Orange Money — utilisé uniquement quand PaymentId est présent (2e appel).</summary>
        public string? OtpCode { get; set; }
    }
}
