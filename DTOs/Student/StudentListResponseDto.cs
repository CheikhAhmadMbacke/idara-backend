namespace Idara.API.DTOs.Student
{
    /// <summary>
    /// Effectifs par régime d'hébergement, pour les onglets de la liste.
    ///
    /// ⚠️ Ces nombres sont calculés PAR LE SERVEUR sur toute l'école, jamais
    /// déduits de la page reçue : la liste est paginée, donc les compter côté
    /// application donnerait, pour un daara de 250 élèves, « Interne (43) »
    /// alors qu'il y en a 110 — un chiffre faux, sans erreur, sur lequel une
    /// école commanderait ses repas.
    ///
    /// Ils tiennent compte des AUTRES filtres actifs (recherche, classe, tarif)
    /// mais PAS du filtre de régime lui-même : un onglet doit annoncer le
    /// nombre d'élèves qu'on verra en tapant dessus.
    /// </summary>
    public class BoardingCountsDto
    {
        public int Total { get; set; }
        public int Boarding { get; set; }
        public int HalfBoarding { get; set; }
        public int Day { get; set; }

        /// <summary>Élèves dont le régime n'est pas renseigné.</summary>
        public int Unset { get; set; }
    }

    public class StudentListResponseDto
    {
        public List<StudentResponseDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)System.Math.Ceiling((double)TotalCount / PageSize) : 0;

        /// <summary>
        /// Effectifs par régime. Renvoyé dans la MÊME réponse que la page :
        /// une seconde requête coûterait un aller-retour de plus (~0,5 s en 3G)
        /// et pourrait renvoyer des chiffres décalés de la liste affichée.
        /// </summary>
        public BoardingCountsDto BoardingCounts { get; set; } = new();
    }
}
