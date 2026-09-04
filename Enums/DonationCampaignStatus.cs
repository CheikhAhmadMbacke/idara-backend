namespace Idara.API.Enums
{
    /// <summary>
    /// État d'une collecte de dons.
    ///
    /// <para>⚠️ « Expirée » n'est PAS un état stocké : une collecte dont la date
    /// limite est passée reste <see cref="Active"/> en base et se lit fermée. Un
    /// état dérivé d'une horloge ne doit jamais être figé en base — il faudrait
    /// alors un cron pour le maintenir, et il mentirait entre deux passages
    /// (même raison que le total collecté, jamais stocké, §112).</para>
    /// </summary>
    public enum DonationCampaignStatus
    {
        /// <summary>Ouverte : la page accepte les dons.</summary>
        Active = 1,

        /// <summary>En pause : le lien vit, la page refuse les dons et le dit.</summary>
        Paused = 2,

        /// <summary>Close : la page remercie et affiche le total. Sans retour.</summary>
        Closed = 3
    }
}
