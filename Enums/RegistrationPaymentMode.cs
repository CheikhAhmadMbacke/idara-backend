namespace Idara.API.Enums
{
    /// <summary>
    /// 💳 Comment la famille règle les frais d'inscription — choisi par l'école
    /// <b>au moment de l'inscription</b>, avant que la facture existe.
    /// </summary>
    /// <remarks>
    /// <para>Ce n'est pas une colonne : rien n'est stocké. Le mode ne vit que le
    /// temps de la création de l'élève, où il décide de ce qui se passe juste
    /// après la facture. Ce qui reste en base, ce sont les FAITS — une facture
    /// en attente d'un côté, une facture soldée plus une entrée de caisse de
    /// l'autre (§207 : un réglage et un fait passé ne se rangent pas au même
    /// endroit).</para>
    /// </remarks>
    public enum RegistrationPaymentMode
    {
        /// <summary>
        /// La facture part en attente, et la famille reçoit son lien de paiement
        /// par SMS. C'est le comportement historique, et celui d'une application
        /// qui n'envoie pas ce champ.
        /// </summary>
        Online = 1,

        /// <summary>
        /// L'argent a été remis au bureau. La facture est créée <b>puis soldée
        /// sur-le-champ</b> : caisse créditée, reçu, aucun rappel de retard.
        ///
        /// <para>🔴 Ne PAS confondre avec « ne pas créer de facture » : l'argent
        /// n'existerait alors nulle part — ni en caisse, ni dans le chiffre
        /// d'affaires — et la famille n'aurait aucune preuve d'avoir payé.</para>
        /// </summary>
        Cash = 2,
    }
}
