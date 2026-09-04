namespace Idara.API.Services.Vision
{
    /// <summary>Ce qu'il faut savoir pour décider si une lecture de cahier a le droit de partir.</summary>
    /// <param name="SchoolId">École à qui imputer la dépense.</param>
    /// <param name="Pages">Nombre d'images de l'envoi.</param>
    public record OcrGuardContext(int SchoolId, int Pages);

    /// <summary>Verdict du garde-fou, avec de quoi le dire à l'école.</summary>
    /// <param name="Allowed">Faux = ne pas appeler le fournisseur.</param>
    /// <param name="BlockedReason">Motif court et stable, écrit au registre.</param>
    /// <param name="UserMessage">Ce qu'on affiche au directeur. Jamais un motif technique.</param>
    /// <param name="RemainingPages">Pages restantes AVANT cet envoi.</param>
    /// <param name="AllowancePages">Quota total de l'école (base + accordées).</param>
    public record OcrGuardDecision(
        bool Allowed,
        string? BlockedReason,
        string? UserMessage,
        int RemainingPages,
        int AllowancePages);

    /// <summary>
    /// Décide si une lecture de cahier peut partir.
    ///
    /// <para><b>Raison d'être.</b> Chaque page envoyée coûte de l'argent réel à
    /// Pyranil. Le coût unitaire est dérisoire (~50 FCFA) et c'est précisément
    /// ce qui rend le garde-fou nécessaire : personne ne remarquera la
    /// différence entre 12 pages et 1 200. Le risque n'est pas le prix, c'est le
    /// volume — exactement la leçon du §191, où un développeur sénégalais a reçu
    /// une facture d'un million de FCFA après qu'un tiers se soit servi de son
    /// compte SMS.</para>
    ///
    /// <para><b>Les compteurs se DÉRIVENT du registre</b> (<c>OcrJobs</c>,
    /// <c>OcrPageGrants</c>), jamais d'une table de totaux : c'est ce qui rend
    /// les plafonds insensibles aux redéploiements.</para>
    ///
    /// <para>🔴 <b>Ce garde-fou échoue FERMÉ — l'inverse de celui des SMS.</b>
    /// <c>ISmsBudgetGuard</c> autorise quand il ne sait pas, parce que couper
    /// toutes les notifications d'un daara pour une requête de comptage ratée
    /// serait un dégât plus fréquent que celui qu'on prévient. Ici c'est
    /// l'inverse : ne pas savoir et laisser passer, c'est dépenser à l'aveugle,
    /// alors que refuser ne coûte au directeur qu'un « réessayez dans un
    /// instant ». Quand la conséquence d'une erreur est une dépense, on refuse ;
    /// quand c'est un silence, on laisse passer.</para>
    /// </summary>
    public interface IOcrBudgetGuard
    {
        Task<OcrGuardDecision> EvaluateAsync(OcrGuardContext ctx, CancellationToken ct = default);

        /// <summary>
        /// État du quota d'une école, pour l'afficher AVANT qu'elle ne
        /// photographie quoi que ce soit. Une limite qu'on découvre après avoir
        /// pris trente photos n'est pas une limite, c'est un piège.
        /// </summary>
        Task<OcrGuardDecision> DescribeAsync(int schoolId, CancellationToken ct = default);
    }
}
