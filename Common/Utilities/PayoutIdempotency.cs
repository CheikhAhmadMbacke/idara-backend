namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Clé d'idempotence des décaissements Wave.
    ///
    /// <para>🔴 C'est la SEULE chose qui empêche un rejeu de sortir l'argent une
    /// seconde fois. Wave garantit qu'une même clé n'exécute qu'une opération ;
    /// deux clés différentes pour le même retrait font deux virements réels.
    /// Elle doit donc être <b>dérivée du retrait</b>, jamais tirée au hasard au
    /// moment de l'appel — sans quoi une reprise après panne repartirait avec
    /// une clé neuve.</para>
    ///
    /// <para>Un retrait relancé après échec porte un nouvel identifiant, donc
    /// une nouvelle clé : c'est voulu, c'est une autre opération.</para>
    /// </summary>
    public static class PayoutIdempotency
    {
        public static string ForWithdrawal(int withdrawalId) => $"idara-withdrawal-{withdrawalId}";
    }
}
