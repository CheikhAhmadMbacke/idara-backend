using System.ComponentModel.DataAnnotations;

namespace Idara.API.Common.Validation
{
    /// <summary>
    /// Variante de <see cref="StringLengthAttribute"/> pour les champs
    /// FACULTATIFS : une chaîne vide ou blanche vaut « non renseigné » et passe,
    /// au lieu d'être rejetée par la borne minimale.
    ///
    /// 🔴 Pourquoi cet attribut existe. <c>StringLength(…, MinimumLength = n)</c>
    /// ne laisse passer que <c>null</c> : <c>""</c> a une longueur de 0, donc il
    /// la rejette. Or un formulaire envoie naturellement <c>controller.text.trim()</c>,
    /// c'est-à-dire <c>""</c> et non <c>null</c>, pour un champ laissé vide. Un
    /// champ facultatif portant une borne minimale devient alors, en pratique,
    /// obligatoire — et l'utilisateur ne lit qu'un « One or more validation
    /// errors occurred » qui ne désigne rien.
    ///
    /// C'est le défaut qui a empêché toute école sans nom arabe de s'inscrire
    /// entre le 2026-08-08 et le 2026-08-31. Même famille que
    /// <see cref="OptionalEmailAddressAttribute"/>, pour lequel le projet avait
    /// déjà dû écrire une variante.
    ///
    /// ⚠️ Règle : sur un <c>string?</c>, ne JAMAIS utiliser
    /// <c>StringLength(MinimumLength = …)</c>. Soit le champ est obligatoire et
    /// porte <c>[Required]</c> (qui rejette <c>""</c> avec un message clair),
    /// soit il est facultatif et porte cet attribut.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class OptionalStringLengthAttribute : ValidationAttribute
    {
        public int MaximumLength { get; }

        /// <summary>
        /// Longueur minimale exigée <b>quand le champ est renseigné</b>. Mesurée
        /// sur la valeur détourée : « deux espaces » n'est pas un nom.
        /// </summary>
        public int MinimumLength { get; set; }

        public OptionalStringLengthAttribute(int maximumLength)
        {
            MaximumLength = maximumLength;
        }

        public override bool IsValid(object? value)
        {
            if (value is null) return true;
            if (value is not string s) return true;

            // Champ laissé vide = non renseigné. C'est tout l'objet de cet
            // attribut ; la règle « ce champ doit être là » se dit ailleurs
            // ([Required], ou une règle croisée comme SchoolNameRule).
            if (string.IsNullOrWhiteSpace(s)) return true;

            // Le maximum porte sur la valeur BRUTE : c'est elle qui part en base
            // et qui doit tenir dans la colonne. Le minimum porte sur la valeur
            // détourée : c'est ce que l'utilisateur a réellement saisi.
            if (s.Length > MaximumLength) return false;
            return s.Trim().Length >= MinimumLength;
        }

        public override string FormatErrorMessage(string name)
            => ErrorMessage ?? $"Le champ {name} doit faire entre {MinimumLength} et {MaximumLength} caractères.";
    }
}
