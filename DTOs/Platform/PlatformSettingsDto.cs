using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Platform
{
    /// <summary>Vue lecture des réglages globaux plateforme.</summary>
    public class PlatformSettingsDto
    {
        public long MinPayinFcfa { get; set; }
        public long MinWithdrawalFcfa { get; set; }
        public double ParentFeePercent { get; set; }
        public double PayoutFeePercent { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>Mise à jour des réglages globaux (SuperAdmin only).</summary>
    public class UpdatePlatformSettingsDto
    {
        [Range(0, 1_000_000, ErrorMessage = "Le montant minimum de paiement doit être entre 0 et 1 000 000.")]
        public long MinPayinFcfa { get; set; }

        [Range(0, 100_000_000, ErrorMessage = "Le montant minimum de retrait doit être entre 0 et 100 000 000.")]
        public long MinWithdrawalFcfa { get; set; }

        [Range(0, 100, ErrorMessage = "La majoration parent (%) doit être entre 0 et 100.")]
        public double ParentFeePercent { get; set; }

        // Borné < 100 strict : ce taux est au DÉNOMINATEUR du calcul du montant
        // envoyé à SenePay (Amount / (1 - taux)), donc 100 % provoquerait une
        // division par zéro. 95 est déjà très au-dessus du réel (~1,77 %).
        [Range(0, 95, ErrorMessage = "Les frais de retrait (%) doivent être entre 0 et 95.")]
        public double PayoutFeePercent { get; set; }
    }
}
