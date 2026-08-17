using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Student
{
    /// <summary>Corps de POST /api/students/{id}/exit.</summary>
    public class StudentExitRequestDto : IValidatableObject
    {
        /// <summary>
        /// Date de sortie. Passée (le parent est venu chercher son enfant sans
        /// prévenir, enregistré plus tard), du jour, ou FUTURE (sortie
        /// programmée : « il part fin juin »). Bornée côté service : jamais
        /// avant l'inscription, jamais au-delà de 24 mois (une faute de frappe
        /// — 2036 pour 2026 — laisserait l'élève facturé indéfiniment).
        /// </summary>
        [Required]
        public DateTime? ExitDate { get; set; }

        [Required]
        public StudentExitReason? Reason { get; set; }

        /// <summary>Obligatoire quand Reason == Other.</summary>
        [StringLength(300)]
        public string? ReasonDetail { get; set; }

        /// <summary>
        /// Annuler les mensualités impayées dont la période commence À PARTIR de
        /// la date de sortie — un enfant parti le 3 juin ne doit rien pour
        /// juillet, mais reste redevable de mai (et de juin, commencé avant son
        /// départ : l'école l'annule à la main si elle veut la remettre).
        /// Refusé si la sortie est FUTURE (les mensualités d'après ne seront
        /// simplement jamais générées) et réservé au SchoolAdmin (aligné sur
        /// l'annulation unitaire de facture).
        /// </summary>
        public bool CancelUnpaidInvoices { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Reason == StudentExitReason.Other && string.IsNullOrWhiteSpace(ReasonDetail))
            {
                yield return new ValidationResult(
                    "Précisez le motif de sortie (motif « Autre »).",
                    new[] { nameof(ReasonDetail) });
            }
        }
    }

    /// <summary>
    /// Réponse de GET /api/students/{id}/exit-preview : ce que la sortie
    /// impliquerait pour les dettes, pour que la case d'annulation ne soit
    /// jamais cochée à l'aveugle.
    /// </summary>
    public class StudentExitPreviewDto
    {
        /// <summary>Mensualités impayées (rien d'encaissé), toutes périodes.</summary>
        public int UnpaidInvoiceCount { get; set; }
        public long UnpaidTotalFcfa { get; set; }

        /// <summary>
        /// Parmi elles, celles que la case annulerait : période commençant à
        /// partir de la date de sortie fournie.
        /// </summary>
        public int CancellableCount { get; set; }
        public long CancellableTotalFcfa { get; set; }

        /// <summary>
        /// Factures PARTIELLEMENT payées — jamais annulées automatiquement
        /// (l'annulation ne rembourse pas, §67) : l'école les traite à la main.
        /// </summary>
        public int PartiallyPaidCount { get; set; }
        public long PartiallyPaidRemainingFcfa { get; set; }
    }
}
