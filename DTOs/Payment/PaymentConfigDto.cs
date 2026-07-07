namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Réglages de paiement lisibles par tout utilisateur authentifié — sert au
    /// client pour AFFICHER le pourcentage de majoration en vigueur et
    /// prévisualiser le montant à payer. Le SuperAdmin peut changer le % à tout
    /// moment (PlatformSettings) ; le client le relit ici → affichage et calcul
    /// s'adaptent sans redéploiement ni valeur codée en dur.
    /// </summary>
    public class PaymentConfigDto
    {
        /// <summary>Majoration parent en pourcentage humain (ex: 7.14 = +7,14 %).</summary>
        public double ParentFeePercent { get; set; }

        /// <summary>Montant minimum d'un paiement (FCFA).</summary>
        public long MinPayinFcfa { get; set; }
    }
}
