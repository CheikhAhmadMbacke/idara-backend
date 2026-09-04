using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Validation;
using Idara.API.Enums;

namespace Idara.API.DTOs.Auth
{
    /// <summary>
    /// Soumission des informations KYC de l'école (documents transmis en base64).
    /// </summary>
    public class SubmitKycRequest : IValidatableObject
    {
        /// <summary>
        /// Nom en français. Plus obligatoire seul : la règle est « au moins l'un
        /// des deux noms » (cf. <see cref="SchoolNameRule"/>), certains daara
        /// n'ayant de nom officiel qu'en arabe.
        /// </summary>
        [OptionalStringLength(200, MinimumLength = 2, ErrorMessage = "Le nom en français doit faire au moins 2 caractères.")]
        public string? SchoolName { get; set; }

        /// <summary>Nom en arabe. Même règle que <see cref="SchoolName"/>.</summary>
        [OptionalStringLength(200, MinimumLength = 2, ErrorMessage = "Le nom en arabe doit faire au moins 2 caractères.")]
        public string? SchoolNameAr { get; set; }

        [Required(ErrorMessage = "L'adresse de l'école est requise.")]
        [StringLength(300)]
        public string SchoolAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le téléphone de l'école est requis.")]
        public string SchoolPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom du représentant est requis.")]
        [StringLength(100)]
        public string RepFirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom du représentant est requis.")]
        [StringLength(100)]
        public string RepLastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le téléphone du représentant est requis.")]
        public string RepPhone { get; set; } = string.Empty;

        /// <summary>
        /// Nature de l'établissement. Facultatif : une app antérieure à ce champ
        /// ne l'envoie pas, et l'école reste alors « non renseignée » plutôt que
        /// classée d'office (cf. <see cref="Enums.SchoolType"/>).
        /// </summary>
        public SchoolType? SchoolType { get; set; }

        /// <summary>
        /// Logo de l'établissement, en base64 (préfixe <c>data:</c> accepté).
        /// </summary>
        /// <remarks>
        /// <para><b>Facultatif, et il doit le rester.</b> Bloquer une inscription
        /// pour une image serait absurde — l'école peut toujours le poser plus
        /// tard depuis la personnalisation de son espace.</para>
        /// <para><b>Pourquoi le demander ICI.</b> Le logo n'est pas décoratif :
        /// il apparaît sur les reçus PDF, les bulletins, l'en-tête de l'espace,
        /// la page publique de paiement et celle des collectes de dons. Le
        /// réclamer au KYC, c'est le récupérer au seul moment où l'école remplit
        /// un formulaire d'identité de bout en bout — au lieu d'espérer qu'elle
        /// trouve, des semaines plus tard, un écran de réglages qu'elle
        /// n'ouvrira jamais.</para>
        /// <para>⚠️ <b>Null = inchangé</b> à la re-soumission d'un KYC rejeté :
        /// une application antérieure à ce champ ne doit pas effacer un logo
        /// déjà posé (§140).</para>
        /// </remarks>
        public string? LogoBase64 { get; set; }

        public List<string> LegalDocumentsBase64 { get; set; } = new();
        public List<string> LegalDocumentsNames { get; set; } = new();
        public List<string> RepresentativeDocumentsBase64 { get; set; } = new();
        public List<string> RepresentativeDocumentsNames { get; set; } = new();

        /// <summary>Le daara doit porter un nom dans au moins une écriture.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
            SchoolNameRule.Validate(SchoolName, SchoolNameAr, nameof(SchoolName), nameof(SchoolNameAr));
    }
}
