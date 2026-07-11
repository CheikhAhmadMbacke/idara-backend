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
