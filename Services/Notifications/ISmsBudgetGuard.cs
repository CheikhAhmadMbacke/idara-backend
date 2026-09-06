using Idara.API.Common.Utilities;
using Idara.API.Enums;

namespace Idara.API.Services.Notifications
{
    /// <summary>Ce qu'il faut savoir pour décider si un SMS a le droit de partir.</summary>
    /// <param name="SchoolId">École à qui imputer la dépense (null hors école).</param>
    /// <param name="RecipientE164">Destinataire déjà normalisé.</param>
    /// <param name="Message">Le texte exact qui partirait — c'est lui qui décide du coût.</param>
    /// <param name="Priority">Ce que l'envoi vaut la peine de coûter.</param>
    /// <param name="AuthorizedCampaign">Envoi de masse DECIDE, chiffre et
    /// confirme a l'avance par la plateforme (campagne SuperAdmin).
    ///
    /// <para>Les plafonds par ecole et le palier souple existent pour attraper ce
    /// que PERSONNE n'a decide : une boucle, un cron emballe, un compte detourne
    /// (§191). Une campagne est l'exact contraire — son volume et son cout sont
    /// affiches et valides avant le premier envoi. Les lui appliquer ne
    /// protegerait de rien et la couperait au tiers du parcours, en laissant la
    /// moitie des familles sans leur lien et sans qu'on sache lesquelles.</para>
    ///
    /// <para>Ce qui reste en vigueur, sans exception : la destination
    /// senegalaise, le coupe-circuit, le palier ABSOLU de la plateforme et le
    /// plafond par DESTINATAIRE. Autrement dit, tout ce qui protege de la
    /// facture catastrophique et du harcelement d'une personne.</para></param>
    public record SmsGuardContext(
        int? SchoolId,
        string RecipientE164,
        string Message,
        SmsPriority Priority,
        bool AuthorizedCampaign = false);

    /// <summary>
    /// Verdict du garde-fou, accompagné du chiffrage — calculé une seule fois et
    /// réutilisé pour écrire le registre, afin que le montant contrôlé et le
    /// montant enregistré ne puissent pas différer.
    /// </summary>
    /// <param name="Allowed">Faux = ne pas appeler le fournisseur.</param>
    /// <param name="BlockedReason">Motif court et stable, écrit au registre.</param>
    /// <param name="Segmentation">Encodage, longueur, segments.</param>
    /// <param name="Network">Réseau du destinataire.</param>
    /// <param name="UnitPriceCentimes">Prix du segment figé à cet instant.</param>
    /// <param name="CostCentimes">Coût estimé si l'envoi a lieu.</param>
    public record SmsGuardDecision(
        bool Allowed,
        string? BlockedReason,
        SmsSegmentation Segmentation,
        SmsNetwork Network,
        long UnitPriceCentimes,
        long CostCentimes);

    /// <summary>
    /// Décide si un SMS peut partir, et à quel coût.
    ///
    /// <para><b>Raison d'être.</b> Avant le 2026-09-01, rien nulle part
    /// n'empêchait Idara d'envoyer cent mille SMS en une nuit : les seuls
    /// compteurs vivaient en mémoire, donc repartaient à zéro à chaque
    /// déploiement, et aucun ne raisonnait en argent. Le risque n'est pas
    /// théorique — un développeur sénégalais a reçu une facture d'un million de
    /// FCFA après qu'un tiers se soit servi de son compte SMS.</para>
    ///
    /// <para><b>Les compteurs se DÉRIVENT du registre</b>
    /// (<c>NotificationLog</c>), jamais d'une table de totaux. Une seule vérité :
    /// ce qui est réellement parti. C'est la discipline du §112 (« P se
    /// recalcule, ne se stocke pas ») appliquée à la dépense SMS, et c'est ce qui
    /// rend les plafonds insensibles aux redémarrages.</para>
    ///
    /// <para><b>Ne lève jamais.</b> Si le garde-fou lui-même échoue (base
    /// indisponible), il AUTORISE l'envoi : couper toutes les notifications d'un
    /// daara parce qu'une requête de comptage a échoué serait un dégât plus
    /// probable et plus fréquent que celui qu'on cherche à prévenir. Le
    /// coupe-circuit global et le plafond du fournisseur restent, eux, en
    /// dernière ligne.</para>
    /// </summary>
    public interface ISmsBudgetGuard
    {
        Task<SmsGuardDecision> EvaluateAsync(SmsGuardContext ctx, CancellationToken ct = default);
    }
}
