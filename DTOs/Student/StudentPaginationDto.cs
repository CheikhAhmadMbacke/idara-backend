using Idara.API.Enums;

namespace Idara.API.DTOs.Student
{
    public class StudentPaginationDto
    {
        public int Page { get; set; } = 1;
        // 25 et non 10 depuis le 2026-07-28 : la compression des réponses étant
        // désormais active côté nginx, une page de 25 élèves coûte à peine plus
        // d'octets qu'une page de 10 — mais épargne deux allers-retours, et
        // c'est la LATENCE qui domine en 3G (~0,5 s par échange depuis Dakar).
        public int PageSize { get; set; } = 25;
        public string? Search { get; set; }
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; } = true;

        // ----- Filtres (2026-08-09) -----
        // Tous OPTIONNELS et cumulatifs (ET logique). Un filtre absent ne
        // restreint rien : une ancienne version de l'app, qui n'en envoie aucun,
        // reçoit exactement la même liste qu'avant.

        /// <summary>Filtre sur le régime d'hébergement. Null = tous.</summary>
        public BoardingStatus? BoardingStatus { get; set; }

        /// <summary>
        /// Élèves dont le régime d'hébergement n'est PAS renseigné. Ne peut pas
        /// être exprimé par <see cref="BoardingStatus"/> (dont la valeur nulle
        /// signifie déjà « pas de filtre »).
        /// </summary>
        public bool? BoardingStatusUnset { get; set; }

        /// <summary>true = seulement les élèves à tarif personnalisé, false = seulement ceux sans. Null = tous.</summary>
        public bool? HasCustomFee { get; set; }

        /// <summary>Filtre sur une classe précise. Null = toutes.</summary>
        public int? ClassId { get; set; }

        /// <summary>
        /// Élèves sans classe affectée. Même raison que
        /// <see cref="BoardingStatusUnset"/> : un <see cref="ClassId"/> nul veut
        /// déjà dire « pas de filtre ».
        /// </summary>
        public bool? WithoutClass { get; set; }

        /// <summary>
        /// true = l'onglet « Sortis » (élèves qui ne font plus partie de
        /// l'effectif). Null/false = l'effectif — le défaut, donc une ancienne
        /// version de l'app qui n'envoie pas le champ voit l'effectif, comme
        /// avant. Les sorties PROGRAMMÉES (date future) restent côté effectif.
        /// </summary>
        public bool? Exited { get; set; }
    }
}
