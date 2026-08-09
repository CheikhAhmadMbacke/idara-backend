namespace Idara.API.DTOs.Export
{
    /// <summary>
    /// Tout ce qu'imprime un reçu de virement (daara → bénéficiaire : salaire,
    /// charge, retrait). Un objet plutôt qu'une liste de paramètres : la méthode
    /// en comptait déjà neuf, et trois s'y ajoutent (numéro du bénéficiaire, nom
    /// arabe du daara, références) — au-delà, deux <c>string</c> voisines
    /// finissent par être interverties sans que rien ne le signale.
    ///
    /// Le MÊME document sert des deux côtés : le daara qui prouve un versement
    /// et le bénéficiaire qui prouve l'avoir reçu. Il ne doit donc rien contenir
    /// qui ne puisse être vu par les deux.
    /// </summary>
    public sealed record TransferReceiptData
    {
        /// <summary>Nom du daara en français (peut être null si le daara n'a qu'un nom arabe).</summary>
        public string? SchoolName { get; init; }

        /// <summary>Nom du daara en arabe, imprimé SOUS le nom français.</summary>
        public string? SchoolNameAr { get; init; }

        public int TransferId { get; init; }

        public string BeneficiaryName { get; init; } = "Bénéficiaire";

        /// <summary>
        /// Numéro du bénéficiaire, déjà formaté pour la lecture (« 77 123 45 67 »).
        /// Absent du reçu jusqu'au 2026-08-08 : un directeur ne pouvait pas
        /// vérifier À QUI le versement était parti.
        /// </summary>
        public string? BeneficiaryPhone { get; init; }

        public long AmountFcfa { get; init; }
        public string OperatorLabel { get; init; } = string.Empty;
        public string CategoryLabel { get; init; } = string.Empty;
        public string StatusLabel { get; init; } = string.Empty;
        public DateTime Date { get; init; }

        /// <summary>Motif libre saisi par l'école. Optionnel.</summary>
        public string? Motif { get; init; }

        /// <summary>Référence Idara (« RET-000087 »).</summary>
        public string Reference { get; init; } = string.Empty;

        /// <summary>Référence du décaissement chez le prestataire. Null si inconnue.</summary>
        public string? ProviderReference { get; init; }
    }
}
