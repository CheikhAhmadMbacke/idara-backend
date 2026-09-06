namespace Idara.API.Options
{
    public class JwtSettings
    {
        public const string SectionName = "Jwt";

        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Durée de vie de l'access token JWT. Elle DOIT rester courte : c'est
        /// elle qui borne le délai de propagation d'une suspension de compte, et
        /// c'est elle qui déclenche la rotation du refresh token — donc le
        /// glissement de la session.
        ///
        /// <para>⚠️ Ancienne clé <c>Jwt:ExpirationDays</c> SUPPRIMÉE le
        /// 2026-09-06 : elle valait 30 en production et rendait le mécanisme de
        /// rafraîchissement mathématiquement inopérant (cf. §223). Un
        /// <c>Jwt__ExpirationDays</c> resté dans l'environnement est désormais
        /// ignoré (le binder de configuration ne lève pas sur une clé inconnue).</para>
        /// </summary>
        public int AccessTokenExpirationMinutes { get; set; } = 60;

        /// <summary>
        /// Durée de vie du refresh token. Elle est GLISSANTE : chaque rotation
        /// en émet un nouveau reparti pour la même durée. Un responsable qui
        /// ouvre l'application au moins une fois dans l'année ne se reconnecte
        /// donc jamais.
        ///
        /// <para>400 jours et non 365 : une famille qui ne rouvre l'application
        /// qu'à la rentrée suivante peut le faire quelques semaines plus tard
        /// que l'année précédente sans être déconnectée pour autant.</para>
        /// </summary>
        public int RefreshTokenExpirationDays { get; set; } = 400;

        /// <summary>
        /// Durée de vie du refresh token des comptes PRIVILÉGIÉS (SuperAdmin).
        /// Le compte qui voit l'argent de toute la plateforme n'a pas le même
        /// public ni le même risque qu'un parent : sa session reste courte.
        /// </summary>
        public int PrivilegedRefreshTokenExpirationDays { get; set; } = 30;

        /// <summary>
        /// Fenêtre de tolérance après une rotation. Si le même refresh token est
        /// présenté deux fois dans ce délai, c'est presque toujours la RÉPONSE
        /// qui s'est perdue (réseau coupé, application tuée par Android entre la
        /// réponse et l'écriture dans le coffre-fort) — pas un rejeu. On réémet
        /// alors dans la même famille au lieu de détruire la session.
        /// </summary>
        public int RefreshRotationGraceSeconds { get; set; } = 120;
    }
}
