namespace Idara.API.Enums
{
    /// <summary>
    /// Ce qu'un SMS vaut la peine de coûter — c'est cette valeur qui décide de
    /// ce qui survit quand un plafond de dépense est atteint (décision
    /// utilisateur 2026-09-01 : deux paliers, pas une coupure sèche).
    ///
    /// <para>Le classement n'est pas une question de confort mais d'enfermement :
    /// couper un rappel de mensualité fait perdre une relance, couper un code de
    /// connexion <b>met un directeur à la porte de son propre daara</b>. Au
    /// palier souple, seul <see cref="Critical"/> passe encore ; au palier
    /// absolu, plus rien ne part, parce qu'une attaque qui viserait justement les
    /// codes de connexion ne doit pas avoir de porte laissée ouverte.</para>
    /// </summary>
    public enum SmsPriority
    {
        /// <summary>
        /// Sans lui, quelqu'un ne peut plus entrer dans l'application : code de
        /// connexion (OTP) et envoi d'identifiants.
        /// </summary>
        Critical = 0,

        /// <summary>
        /// Message d'argent adressé à une personne précise à la suite de son
        /// propre geste : paiement reçu, encaissement annulé, transfert arrivé.
        /// Sa perte se rattrape (l'information est aussi dans l'application),
        /// mais elle se remarque.
        /// </summary>
        Normal = 1,

        /// <summary>
        /// Envoi de masse déclenché par un automate : émission des mensualités,
        /// rappel avant échéance, rappel de retard. C'est le gros du volume,
        /// donc le premier à couper — et le seul dont la perte se rattrape
        /// entièrement au tour suivant.
        /// </summary>
        Bulk = 2,
    }
}
