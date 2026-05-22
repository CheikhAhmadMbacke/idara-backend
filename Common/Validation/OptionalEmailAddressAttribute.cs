using System.ComponentModel.DataAnnotations;

namespace Idara.API.Common.Validation
{
    /// <summary>
    /// Variante de <see cref="EmailAddressAttribute"/> qui accepte aussi les
    /// chaînes vides / blanches. Utile sur les DTOs de mise à jour partielle
    /// où le formulaire envoie "" pour vider un champ optionnel.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class OptionalEmailAddressAttribute : ValidationAttribute
    {
        private static readonly EmailAddressAttribute _inner = new();

        public override bool IsValid(object? value)
        {
            if (value is null) return true;
            if (value is string s && string.IsNullOrWhiteSpace(s)) return true;
            return _inner.IsValid(value);
        }

        public override string FormatErrorMessage(string name)
            => ErrorMessage ?? $"Le champ {name} n'est pas une adresse e-mail valide.";
    }
}
