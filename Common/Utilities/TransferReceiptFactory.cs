using Idara.API.DTOs.Export;
using Idara.API.Models;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Compose le reçu d'un virement à partir de l'entité.
    ///
    /// SOURCE UNIQUE des deux endpoints qui servent CE MÊME document : celui du
    /// daara (preuve de versement) et celui du bénéficiaire (preuve d'encaissement).
    /// Ils construisaient chacun leur libellé de catégorie — deux copies d'un
    /// switch que <see cref="FinanceLabels.TransferCategory"/> fournissait déjà,
    /// et déjà divergentes (« Fournisseur » d'un côté, « Achat / fournisseur » de
    /// l'autre). Deux personnes lisaient donc deux natures différentes pour le
    /// même virement.
    /// </summary>
    public static class TransferReceiptFactory
    {
        public static TransferReceiptData From(Withdrawal w) => new()
        {
            SchoolName = w.School?.Name,
            SchoolNameAr = w.School?.NameAr,
            TransferId = w.Id,
            BeneficiaryName = string.IsNullOrWhiteSpace(w.RecipientName)
                ? "Bénéficiaire"
                : w.RecipientName.Trim(),
            // Numéro lisible « 77 123 45 67 » : c'est ainsi qu'un directeur le
            // reconnaît, pas en E.164.
            BeneficiaryPhone = SenegalPhone.ToDisplay(w.RecipientPhone, fallback: string.Empty),
            AmountFcfa = w.AmountFcfa,
            OperatorLabel = FinanceLabels.Operator(w.Operator),
            CategoryLabel = FinanceLabels.TransferCategory(w.Category, w.CategoryLabel),
            StatusLabel = FinanceLabels.WithdrawalStatus(w.Status),
            Date = w.CompletedAt ?? w.CreatedAt,
            Motif = w.Motif,
            Reference = IdaraReference.Withdrawal(w.Id),
            ProviderReference = w.SenePayDisbursementId
        };
    }
}
