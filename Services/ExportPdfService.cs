using Idara.API.DTOs.Payment;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Idara.API.Services
{
    /// <summary>
    /// Génère des PDF « tableau » à partager (WhatsApp) côté école :
    /// suivi des mensualités (roster) et rapport de dons par donateur. Rendu en
    /// mémoire (byte[]), pas d'écriture disque (documents éphémères, régénérables).
    /// Calqué sur <see cref="SubscriptionInvoicePdfService"/> pour le style.
    /// </summary>
    public interface IExportPdfService
    {
        byte[] BuildRosterPdf(string schoolName, int year, int month, PaymentRosterResponseDto roster);
        byte[] BuildDonorReportPdf(
            string schoolName, string donorName, DateTime? from, DateTime? to,
            IReadOnlyList<(DateTime Date, long Amount)> donations, long total);

        /// <summary>
        /// Reçu de virement (F3) : daara → bénéficiaire (salaire / charge). Rendu
        /// en mémoire (byte[]), partageable WhatsApp par le bénéficiaire.
        /// </summary>
        byte[] BuildTransferReceiptPdf(
            string schoolName, int transferId, string beneficiaryName, long amountFcfa,
            string operatorLabel, string categoryLabel, string statusLabel, DateTime date);

        /// <summary>
        /// Rapport financier périodique (F4) : total entrées / sorties / net +
        /// ventilation par catégorie + rappel wallet SenePay + solde global.
        /// </summary>
        byte[] BuildFinanceReportPdf(
            string schoolName, DateTime? from, DateTime? to,
            IReadOnlyList<(string Category, long Amount)> incomeByCategory,
            IReadOnlyList<(string Category, long Amount)> expenseByCategory,
            long totalIncome, long totalExpense,
            long walletAvailableFcfa, long globalBalanceFcfa);
    }

    public class ExportPdfService : IExportPdfService
    {
        private const string PrimaryHex = "#0B744D";
        private const string TextPrimary = "#0F172A";
        private const string TextSecondary = "#475569";
        private const string Border = "#E2E8F0";
        private const string SurfaceVariant = "#F8FAFC";
        private const string AmberHex = "#B45309";
        private const string RedHex = "#B91C1C";

        private static readonly string[] FrMonths =
        {
            "janvier", "fevrier", "mars", "avril", "mai", "juin",
            "juillet", "aout", "septembre", "octobre", "novembre", "decembre"
        };

        public byte[] BuildRosterPdf(string schoolName, int year, int month, PaymentRosterResponseDto roster)
        {
            var monthLabel = $"{FrMonths[month - 1]} {year}";
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.4f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(9).FontColor(TextPrimary));

                    page.Header().Element(c => Header(c, schoolName, "Suivi des paiements", monthLabel));
                    page.Content().Element(c => RosterContent(c, roster));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void RosterContent(IContainer container, PaymentRosterResponseDto roster)
        {
            container.Column(col =>
            {
                col.Item().PaddingBottom(8).Row(row =>
                {
                    Counter(row, "A jour", roster.PaidCount, PrimaryHex);
                    Counter(row, "En attente", roster.PendingCount, AmberHex);
                    Counter(row, "En retard", roster.OverdueCount, RedHex);
                    Counter(row, "Sans facture", roster.NoInvoiceCount, TextSecondary);
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        void H(string s) => header.Cell().Background(PrimaryHex)
                            .PaddingVertical(4).PaddingHorizontal(5).Text(s).FontColor(Colors.White).SemiBold();
                        H("Eleve");
                        H("Classe");
                        H("Statut");
                        header.Cell().Background(PrimaryHex).PaddingVertical(4).PaddingHorizontal(5)
                            .AlignRight().Text("Du").FontColor(Colors.White).SemiBold();
                        header.Cell().Background(PrimaryHex).PaddingVertical(4).PaddingHorizontal(5)
                            .AlignRight().Text("Paye").FontColor(Colors.White).SemiBold();
                    });

                    foreach (var e in roster.Entries)
                    {
                        var name = $"{e.StudentFirstName} {e.StudentLastName}".Trim();
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(5).Text(name);
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(5)
                            .Text(e.ClassName ?? "-").FontSize(8);
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(5)
                            .Text(StatusLabel(e.Status)).FontColor(StatusColor(e.Status)).SemiBold().FontSize(8);
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(5)
                            .AlignRight().Text($"{e.AmountDueFcfa:N0}").FontSize(8);
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(5)
                            .AlignRight().Text($"{e.AmountPaidFcfa:N0}").FontSize(8);
                    }
                });
            });
        }

        private static void Counter(RowDescriptor row, string label, int count, string color)
        {
            row.RelativeItem().PaddingRight(4).Background(SurfaceVariant).Padding(6).Column(c =>
            {
                c.Item().Text($"{count}").Bold().FontSize(14).FontColor(color);
                c.Item().Text(label).FontSize(8).FontColor(TextSecondary);
            });
        }

        // Variante « montant » : affiche un texte formaté (ex. "12 000 FCFA")
        // au lieu d'un simple compteur entier.
        private static void MoneyCounter(RowDescriptor row, string label, string value, string color)
        {
            row.RelativeItem().PaddingRight(4).Background(SurfaceVariant).Padding(6).Column(c =>
            {
                c.Item().Text($"{value} FCFA").Bold().FontSize(13).FontColor(color);
                c.Item().Text(label).FontSize(8).FontColor(TextSecondary);
            });
        }

        public byte[] BuildDonorReportPdf(
            string schoolName, string donorName, DateTime? from, DateTime? to,
            IReadOnlyList<(DateTime Date, long Amount)> donations, long total)
        {
            var period = (from.HasValue ? from.Value.ToString("dd/MM/yyyy") : "...") + " -> " +
                         (to.HasValue ? to.Value.ToString("dd/MM/yyyy") : "...");
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary));

                    page.Header().Element(c => Header(c, schoolName, "Rapport de dons", period));
                    page.Content().Element(c => DonorContent(c, donorName, donations, total));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void DonorContent(IContainer container, string donorName,
            IReadOnlyList<(DateTime Date, long Amount)> donations, long total)
        {
            container.Column(col =>
            {
                col.Item().Background(SurfaceVariant).Padding(10).Text(t =>
                {
                    t.Span("Donateur : ").SemiBold().FontColor(TextSecondary);
                    t.Span(donorName);
                });

                col.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6)
                            .Text("Date").FontColor(Colors.White).SemiBold();
                        header.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6)
                            .AlignRight().Text("Montant").FontColor(Colors.White).SemiBold();
                    });

                    foreach (var d in donations)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6)
                            .Text($"{d.Date:dd/MM/yyyy}");
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6)
                            .AlignRight().Text($"{d.Amount:N0} FCFA");
                    }

                    table.Cell().PaddingVertical(6).PaddingHorizontal(6).Text("Total").Bold();
                    table.Cell().PaddingVertical(6).PaddingHorizontal(6).AlignRight()
                        .Text($"{total:N0} FCFA").Bold().FontColor(PrimaryHex);
                });

                if (donations.Count == 0)
                    col.Item().PaddingTop(10).Text("Aucun don sur la periode.").FontColor(TextSecondary).Italic();
            });
        }

        public byte[] BuildTransferReceiptPdf(
            string schoolName, int transferId, string beneficiaryName, long amountFcfa,
            string operatorLabel, string categoryLabel, string statusLabel, DateTime date)
        {
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary));

                    page.Header().Element(c => Header(c, schoolName, "Recu de virement", $"N {transferId:D6}"));
                    page.Content().Element(c => TransferContent(
                        c, beneficiaryName, amountFcfa, operatorLabel, categoryLabel, statusLabel, date));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void TransferContent(
            IContainer container, string beneficiaryName, long amountFcfa,
            string operatorLabel, string categoryLabel, string statusLabel, DateTime date)
        {
            container.Column(col =>
            {
                col.Item().Background(SurfaceVariant).Padding(10).Column(c =>
                {
                    c.Item().Text(t =>
                    {
                        t.Span("Beneficiaire : ").SemiBold().FontColor(TextSecondary);
                        t.Span(beneficiaryName).Bold();
                    });
                    c.Item().Text(t =>
                    {
                        t.Span("Nature : ").SemiBold().FontColor(TextSecondary);
                        t.Span(categoryLabel);
                    });
                    c.Item().Text(t =>
                    {
                        t.Span("Date : ").SemiBold().FontColor(TextSecondary);
                        t.Span($"{date:dd/MM/yyyy}");
                    });
                });

                col.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                    });

                    table.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6)
                        .Text("Montant recu").FontColor(Colors.White).SemiBold();
                    table.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                        .Text($"{amountFcfa:N0} FCFA").FontColor(Colors.White).SemiBold();

                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6)
                        .Text("Moyen");
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                        .Text(operatorLabel);

                    table.Cell().PaddingVertical(6).PaddingHorizontal(6).Text("Statut").Bold();
                    table.Cell().PaddingVertical(6).PaddingHorizontal(6).AlignRight()
                        .Text(statusLabel).Bold().FontColor(PrimaryHex);
                });

                col.Item().PaddingTop(14).Text(
                    "Document genere electroniquement, sans signature. Ce recu atteste du virement recu.")
                    .FontSize(8).FontColor(TextSecondary);
            });
        }

        public byte[] BuildFinanceReportPdf(
            string schoolName, DateTime? from, DateTime? to,
            IReadOnlyList<(string Category, long Amount)> incomeByCategory,
            IReadOnlyList<(string Category, long Amount)> expenseByCategory,
            long totalIncome, long totalExpense,
            long walletAvailableFcfa, long globalBalanceFcfa)
        {
            var period = (from.HasValue ? from.Value.ToString("dd/MM/yyyy") : "...") + " -> " +
                         (to.HasValue ? to.Value.ToString("dd/MM/yyyy") : "...");
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary));

                    page.Header().Element(c => Header(c, schoolName, "Rapport financier", period));
                    page.Content().Element(c => FinanceContent(
                        c, incomeByCategory, expenseByCategory, totalIncome, totalExpense,
                        walletAvailableFcfa, globalBalanceFcfa));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void FinanceContent(
            IContainer container,
            IReadOnlyList<(string Category, long Amount)> incomeByCategory,
            IReadOnlyList<(string Category, long Amount)> expenseByCategory,
            long totalIncome, long totalExpense, long walletAvailableFcfa, long globalBalanceFcfa)
        {
            var net = totalIncome - totalExpense;
            container.Column(col =>
            {
                // Bandeau de synthèse
                col.Item().PaddingBottom(8).Row(row =>
                {
                    MoneyCounter(row, "Entrees", $"{totalIncome:N0}", PrimaryHex);
                    MoneyCounter(row, "Sorties", $"{totalExpense:N0}", RedHex);
                    MoneyCounter(row, "Net", $"{net:N0}", net >= 0 ? PrimaryHex : RedHex);
                });

                CategoryTable(col, "Entrees par categorie", incomeByCategory, totalIncome, PrimaryHex);
                col.Item().PaddingTop(10);
                CategoryTable(col, "Sorties par categorie", expenseByCategory, totalExpense, RedHex);

                col.Item().PaddingTop(14).Background(SurfaceVariant).Padding(10).Column(c =>
                {
                    c.Item().Row(r =>
                    {
                        r.RelativeItem().Text("Wallet SenePay disponible").FontColor(TextSecondary);
                        r.ConstantItem(120).AlignRight().Text($"{walletAvailableFcfa:N0} FCFA").SemiBold();
                    });
                    c.Item().PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().Text("Solde global du daara").Bold();
                        r.ConstantItem(120).AlignRight().Text($"{globalBalanceFcfa:N0} FCFA").Bold().FontColor(PrimaryHex);
                    });
                });
            });
        }

        private static void CategoryTable(
            ColumnDescriptor col, string title,
            IReadOnlyList<(string Category, long Amount)> rows, long total, string color)
        {
            col.Item().Text(title).SemiBold().FontColor(TextSecondary).FontSize(11);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Background(color).PaddingVertical(4).PaddingHorizontal(6)
                        .Text("Categorie").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(color).PaddingVertical(4).PaddingHorizontal(6)
                        .AlignRight().Text("Montant").FontColor(Colors.White).SemiBold();
                });

                if (rows.Count == 0)
                {
                    table.Cell().ColumnSpan(2).BorderBottom(1).BorderColor(Border)
                        .PaddingVertical(4).PaddingHorizontal(6)
                        .Text("Aucun mouvement.").FontColor(TextSecondary).Italic().FontSize(9);
                }
                else
                {
                    foreach (var (cat, amount) in rows)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(6)
                            .Text(string.IsNullOrWhiteSpace(cat) ? "Sans categorie" : cat).FontSize(9);
                        table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(6)
                            .AlignRight().Text($"{amount:N0} FCFA").FontSize(9);
                    }
                }

                table.Cell().PaddingVertical(5).PaddingHorizontal(6).Text("Total").Bold();
                table.Cell().PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                    .Text($"{total:N0} FCFA").Bold().FontColor(color);
            });
        }

        private static void Header(IContainer container, string schoolName, string title, string subtitle)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(schoolName).Bold().FontSize(13).FontColor(TextPrimary);
                        c.Item().Text("via Idara").FontSize(8).FontColor(TextSecondary);
                    });
                    row.ConstantItem(190).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text(title).Bold().FontSize(12).FontColor(PrimaryHex);
                        c.Item().AlignRight().Text(subtitle).FontSize(9).FontColor(TextSecondary);
                    });
                });
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(PrimaryHex);
                col.Item().PaddingBottom(8);
            });
        }

        private static void Footer(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Edite par ").FontColor(TextSecondary).FontSize(7);
                    t.Span("Idara").Bold().FontColor(PrimaryHex).FontSize(7);
                    t.Span($" - {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC").FontColor(TextSecondary).FontSize(7);
                });
                row.ConstantItem(70).AlignRight().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(7).FontColor(TextSecondary);
                    t.Span(" / ").FontSize(7).FontColor(TextSecondary);
                    t.TotalPages().FontSize(7).FontColor(TextSecondary);
                });
            });
        }

        private static string StatusLabel(RosterPaymentStatus s) => s switch
        {
            RosterPaymentStatus.Paid => "A jour",
            RosterPaymentStatus.Pending => "En attente",
            RosterPaymentStatus.Overdue => "En retard",
            _ => "Sans facture"
        };

        private static string StatusColor(RosterPaymentStatus s) => s switch
        {
            RosterPaymentStatus.Paid => PrimaryHex,
            RosterPaymentStatus.Pending => AmberHex,
            RosterPaymentStatus.Overdue => RedHex,
            _ => TextSecondary
        };
    }
}
