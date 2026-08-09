using System.ComponentModel.DataAnnotations;

namespace Idara.API.Common.Validation
{
    /// <summary>
    /// Règle unique du nom d'un daara : il existe en français, en arabe, ou dans
    /// les deux — mais jamais dans aucune des deux. Partagée par les DEUX points
    /// de saisie (soumission KYC à l'inscription, édition ultérieure par l'école
    /// ou le SuperAdmin) pour qu'ils ne puissent pas diverger.
    ///
    /// ⚠️ La règle n'est PAS portée par une contrainte en base : elle mordrait sur
    /// les écoles créées avant l'ajout du nom arabe, et sur toute ligne écrite par
    /// un autre chemin (seed, restauration de sauvegarde). Elle est vérifiée à
    /// l'entrée, là où un humain saisit.
    /// </summary>
    public static class SchoolNameRule
    {
        public const string Message =
            "Renseignez le nom du daara en français ou en arabe — au moins l'un des deux.";

        /// <summary>
        /// Renvoie l'erreur rattachée aux DEUX champs quand aucun n'est renseigné.
        /// Les rattacher tous les deux permet à l'app de les surligner ensemble :
        /// c'est la paire qui est invalide, aucun des deux n'est fautif seul.
        /// </summary>
        public static IEnumerable<ValidationResult> Validate(
            string? nameFr, string? nameAr,
            string frFieldName = "Name", string arFieldName = "NameAr")
        {
            if (string.IsNullOrWhiteSpace(nameFr) && string.IsNullOrWhiteSpace(nameAr))
                yield return new ValidationResult(Message, new[] { frFieldName, arFieldName });
        }
    }
}
