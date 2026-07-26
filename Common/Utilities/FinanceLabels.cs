using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Libellés FRANÇAIS des enums financiers, pour les documents PDF générés par
    /// le serveur (exports d'historiques, reçus, rapports). L'app, elle, traduit
    /// ces mêmes enums côté client via i18n FR/AR — ici on est dans un document
    /// figé, en français, comme tous les autres PDF d'Idara.
    ///
    /// Source UNIQUE : si un libellé change, il change partout. Volontairement
    /// sans accents, pour rester homogène avec les PDF existants.
    /// </summary>
    public static class FinanceLabels
    {
        /// <summary>
        /// Plafond de lignes d'un export d'historique PDF. Sans plafond, une école
        /// de plusieurs années générerait un document de centaines de pages (et une
        /// requête lourde). Le plafond n'est JAMAIS silencieux : quand il est
        /// atteint, le titre du document le dit (cf. <see cref="ExportTitle"/>).
        /// </summary>
        public const int MaxExportRows = 2000;

        /// <summary>Titre du document, complété si l'export a été plafonné.</summary>
        public static string ExportTitle(string baseTitle, int rowCount) =>
            rowCount >= MaxExportRows ? $"{baseTitle} ({MaxExportRows} plus recentes)" : baseTitle;

        public static string Operator(PaymentOperator op) => op switch
        {
            PaymentOperator.Wave => "Wave",
            PaymentOperator.Orange => "Orange Money",
            _ => op.ToString()
        };

        public static string PaymentStatus(Enums.PaymentStatus status) => status switch
        {
            Enums.PaymentStatus.Completed => "Reussi",
            Enums.PaymentStatus.Pending => "En cours",
            Enums.PaymentStatus.Failed => "Echoue",
            Enums.PaymentStatus.Cancelled => "Annule",
            Enums.PaymentStatus.Expired => "Expire",
            _ => status.ToString()
        };

        public static string WithdrawalStatus(Enums.WithdrawalStatus status) => status switch
        {
            Enums.WithdrawalStatus.Completed => "Effectue",
            Enums.WithdrawalStatus.Initiated => "En cours",
            Enums.WithdrawalStatus.UnderVerification => "En verification",
            Enums.WithdrawalStatus.Failed => "Echoue",
            Enums.WithdrawalStatus.Cancelled => "Annule",
            _ => status.ToString()
        };

        /// <summary>Nature d'un transfert sortant ; <paramref name="customLabel"/> (saisi) l'emporte.</summary>
        public static string TransferCategory(Enums.TransferCategory category, string? customLabel = null)
        {
            if (!string.IsNullOrWhiteSpace(customLabel)) return customLabel!;
            return category switch
            {
                Enums.TransferCategory.TeacherSalary => "Salaire enseignant",
                Enums.TransferCategory.StaffSalary => "Salaire personnel",
                Enums.TransferCategory.SalaryAdvance => "Avance sur salaire",
                Enums.TransferCategory.Rent => "Loyer",
                Enums.TransferCategory.Supplier => "Achat / fournisseur",
                Enums.TransferCategory.Utilities => "Factures et abonnements",
                Enums.TransferCategory.Food => "Nourriture / repas",
                Enums.TransferCategory.Equipment => "Materiel et equipement",
                Enums.TransferCategory.Maintenance => "Travaux et entretien",
                Enums.TransferCategory.Transport => "Transport",
                Enums.TransferCategory.Health => "Sante / soins",
                Enums.TransferCategory.AdminFees => "Frais administratifs",
                Enums.TransferCategory.Other => "Autre",
                _ => "Retrait"
            };
        }

        /// <summary>Nature d'une ligne du wallet école (badge de l'historique).</summary>
        public static string WalletSource(Enums.WalletSource source) => source switch
        {
            Enums.WalletSource.Payment => "Paiement recu",
            Enums.WalletSource.Donation => "Don recu",
            Enums.WalletSource.Topup => "Recharge du wallet",
            Enums.WalletSource.Subscription => "Abonnement Idara",
            Enums.WalletSource.Withdrawal => "Retrait / virement",
            Enums.WalletSource.Adjustment => "Ajustement",
            _ => "Mouvement"
        };

        /// <summary>Type de mouvement wallet, précisé quand il ne va pas de soi.</summary>
        public static string WalletTransactionType(Enums.WalletTransactionType type) => type switch
        {
            Enums.WalletTransactionType.Credit => "Credit",
            Enums.WalletTransactionType.Debit => "Debit",
            Enums.WalletTransactionType.Reservation => "Montant reserve",
            Enums.WalletTransactionType.Release => "Montant restitue",
            Enums.WalletTransactionType.Adjustment => "Correction",
            _ => type.ToString()
        };

        public static string PaymentPurpose(Enums.PaymentPurpose purpose) => purpose switch
        {
            Enums.PaymentPurpose.WalletTopup => "Recharge du wallet",
            Enums.PaymentPurpose.Donation => "Don",
            _ => "Paiement mensualite"
        };
    }
}
