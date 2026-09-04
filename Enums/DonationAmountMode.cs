namespace Idara.API.Enums
{
    /// <summary>Qui décide du montant d'un don sur une collecte.</summary>
    public enum DonationAmountMode
    {
        /// <summary>Le donateur saisit ce qu'il veut (bornes plateforme).</summary>
        Free = 1,

        /// <summary>L'école impose un montant unique (parrainage, part fixe).</summary>
        Fixed = 2
    }
}
