using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Observability
{
    /// <summary>
    /// Ce que l'application envoie quand elle plante ou quand l'utilisateur
    /// appuie sur « Signaler un problème ».
    ///
    /// <para><b>Toutes les longueurs sont bornées ici et pas seulement en base</b> :
    /// l'endpoint de réception est, par nature, un vecteur de déni de service.
    /// Sans plafond déclaré, un appareil en boucle d'erreur remplirait le disque
    /// (§4.8, garde-fou 3). La validation automatique d'<c>[ApiController]</c>
    /// rejette donc avant même d'atteindre la base.</para>
    /// </summary>
    public class ReportIncidentDto
    {
        /// <summary>
        /// Code d'incident généré par l'application et déjà affiché à
        /// l'utilisateur. On le respecte quand il est au bon format : c'est ce
        /// code-là que le directeur va nous dicter. Vide = le serveur en génère un.
        /// </summary>
        [StringLength(20)]
        public string? Code { get; set; }

        /// <summary>0 = plantage, 1 = signalement, 2 = erreur d'API,
        /// 3 = redémarrage inattendu.</summary>
        [Range(0, 3)]
        public int Kind { get; set; }

        [StringLength(20)]
        public string? Platform { get; set; }

        [StringLength(40)]
        public string? AppVersion { get; set; }

        [StringLength(120)]
        public string? Device { get; set; }

        [StringLength(10)]
        public string? LocaleCode { get; set; }

        /// <summary>Route de l'écran courant — jamais un libellé de données.</summary>
        [StringLength(200)]
        public string? Route { get; set; }

        [StringLength(400)]
        public string? Message { get; set; }

        [StringLength(160)]
        public string? ExceptionType { get; set; }

        [StringLength(8000)]
        public string? StackTrace { get; set; }

        /// <summary>Code de corrélation de la requête HTTP fautive, s'il y en a une.</summary>
        [StringLength(20)]
        public string? RequestTrace { get; set; }

        /// <summary>Ce que l'utilisateur décrit lui-même. Facultatif.</summary>
        [StringLength(600)]
        public string? UserComment { get; set; }
    }
}
