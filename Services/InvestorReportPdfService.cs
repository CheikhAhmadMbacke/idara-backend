using Idara.API.Common.Utilities;
using Idara.API.DTOs.Admin;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Idara.API.Services
{
    /// <summary>
    /// « Rapport investisseur » : le document PDF que le fondateur remet en
    /// preuve — KPIs du moment + série mensuelle complète (CA plateforme,
    /// volume traité, croissance du parc). Rendu en mémoire (byte[]), jamais
    /// écrit sous wwwroot (§122 : rien de financier dans un dossier servi par
    /// nginx). Style calqué sur <see cref="ExportPdfService"/> (§120 : paysage,
    /// une colonne par information, ShowEntire, en-tête répété).
    /// </summary>
    public interface IInvestorReportPdfService
    {
        byte[] Build(InvestorMetricsDto data);
    }

    public class InvestorReportPdfService : IInvestorReportPdfService
    {
        private const string PrimaryHex = "#0B744D";
        private const string TextPrimary = "#0F172A";
        private const string TextSecondary = "#475569";
        private const string Border = "#E2E8F0";
        private const string SurfaceVariant = "#F8FAFC";

        private static readonly string[] FrMonths =
        {
            "janv.", "févr.", "mars", "avril", "mai", "juin",
            "juil.", "août", "sept.", "oct.", "nov.", "déc."
        };

        private static readonly string[] FrMonthsLong =
        {
            "janvier", "février", "mars", "avril", "mai", "juin",
            "juillet", "août", "septembre", "octobre", "novembre", "décembre"
        };

        public byte[] Build(InvestorMetricsDto data)
        {
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(8.5f).FontColor(TextPrimary).Bilingual());

                    page.Header().Element(c => Header(c, data.GeneratedAt));
                    page.Content().Element(c => Content(c, data));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void Content(IContainer container, InvestorMetricsDto data)
        {
            container.Column(col =>
            {
                var k = data.Kpis;

                // --- Bandeau : les 4 chiffres qu'un investisseur lit d'abord ---
                col.Item().PaddingBottom(6).Row(row =>
                {
                    Kpi(row, "Revenu récurrent mensuel (MRR)", $"{Fmt(k.MrrActiveFcfa)} FCFA",
                        $"{k.SchoolsActivePaying} école(s) payante(s) · ARPU {Fmt(k.ArpuFcfa)} FCFA");
                    Kpi(row, "CA plateforme cumulé", $"{Fmt(k.GrossRevenueTotalFcfa)} FCFA",
                        $"Abonnements {Fmt(k.SubscriptionRevenueTotalFcfa)} + marge paiements {Fmt(k.PaymentMarginTotalFcfa)}");
                    Kpi(row, "Volume traité en ligne", $"{Fmt(k.GmvOnlineTotalFcfa)} FCFA",
                        $"{k.PaymentsOnlineCountTotal} paiement(s) Wave / Orange Money");
                    Kpi(row, "Parc", $"{k.SchoolsValidatedTotal} écoles · {k.StudentsActiveTotal} élèves",
                        $"{k.GuardianAccountsTotal} comptes parents · {k.TeacherStaffAccountsTotal} enseignants/personnel");
                });

                // --- Détail du parc + pipeline ---
                col.Item().PaddingBottom(6).Background(SurfaceVariant).Padding(6).Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Abonnements : ").SemiBold().FontColor(TextSecondary);
                        t.Span($"{k.SchoolsActivePaying} actifs · {k.SchoolsInTrial} en essai "
                            + $"(pipeline {Fmt(k.MrrPipelineFcfa)} FCFA/mois) · "
                            + $"{k.SchoolsInArrears} en impayé · {k.SchoolsSuspended} suspendus. ")
                            .FontColor(TextSecondary);
                        t.Span("Espèces gérées dans l'outil : ").SemiBold().FontColor(TextSecondary);
                        t.Span($"{Fmt(k.GmvCashTotalFcfa)} FCFA encaissés au guichet des écoles.")
                            .FontColor(TextSecondary);
                    });
                });

                // --- Série mensuelle ---
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.5f);  // Mois
                        c.RelativeColumn(1.2f);  // CA abonnements
                        c.RelativeColumn(1.2f);  // Marge paiements
                        c.RelativeColumn(1.2f);  // CA total
                        c.RelativeColumn(1.1f);  // Frais payout
                        c.RelativeColumn(1.2f);  // CA net
                        c.RelativeColumn(1.4f);  // Volume en ligne
                        c.RelativeColumn(0.7f);  // Nb
                        c.RelativeColumn(1.2f);  // Espèces
                        c.RelativeColumn(1.2f);  // Écoles
                        c.RelativeColumn(1.3f);  // Élèves
                        c.RelativeColumn(1.2f);  // Parents
                    });

                    table.Header(h =>
                    {
                        HeaderCell(h, "Mois");
                        HeaderCell(h, "CA abonnements", true);
                        HeaderCell(h, "Marge paiements", true);
                        HeaderCell(h, "CA total", true);
                        HeaderCell(h, "Frais payout", true);
                        HeaderCell(h, "CA net", true);
                        HeaderCell(h, "Volume en ligne", true);
                        HeaderCell(h, "Nb", true);
                        HeaderCell(h, "Espèces", true);
                        HeaderCell(h, "Écoles (nouv. / cumul)", true);
                        HeaderCell(h, "Élèves (nouv. / cumul)", true);
                        HeaderCell(h, "Parents (nouv. / cumul)", true);
                    });

                    var zebra = false;
                    foreach (var m in data.Months)
                    {
                        var bg = zebra ? SurfaceVariant : "#FFFFFF";
                        zebra = !zebra;
                        var label = $"{FrMonths[m.Month - 1]} {m.Year}"
                            + (m.IsCurrentPartialMonth ? " (en cours)" : "");

                        Cell(table, bg).Text(label).SemiBold();
                        Money(table, bg, m.SubscriptionRevenueFcfa);
                        Money(table, bg, m.PaymentMarginFcfa);
                        Cell(table, bg).AlignRight().Text(Fmt(m.GrossRevenueFcfa)).SemiBold();
                        Money(table, bg, m.PayoutFeesFcfa);
                        Cell(table, bg).AlignRight().Text(Fmt(m.NetRevenueFcfa)).SemiBold()
                            .FontColor(PrimaryHex);
                        Money(table, bg, m.GmvOnlineFcfa);
                        Cell(table, bg).AlignRight().Text($"{m.PaymentsOnlineCount}");
                        Money(table, bg, m.GmvCashFcfa);
                        Cell(table, bg).AlignRight().Text($"+{m.NewSchools} / {m.CumulativeSchools}");
                        Cell(table, bg).AlignRight().Text($"+{m.NewStudents} / {m.CumulativeStudents}");
                        Cell(table, bg).AlignRight()
                            .Text($"+{m.NewGuardianAccounts} / {m.CumulativeGuardianAccounts}");
                    }
                });

                if (data.Months.Count == 0)
                    col.Item().PaddingTop(8).Text("Aucune activité enregistrée pour l'instant.")
                        .FontColor(TextSecondary);

                // --- Méthodologie : ce qui rend le document opposable ---
                col.Item().PaddingTop(8).Text(t =>
                {
                    t.Span("Méthodologie : ").SemiBold().FontSize(7.5f).FontColor(TextSecondary);
                    t.Span("montants en FCFA. Chiffres recalculés à la génération depuis la comptabilité "
                        + "de la plateforme (paiements aboutis, factures d'abonnement encaissées, retraits) — "
                        + "vérifiables ligne à ligne. CA = abonnements encaissés + marge sur les paiements en ligne. "
                        + "Volume en ligne = montants payés via Wave / Orange Money, tous motifs. "
                        + "Le mois en cours est partiel. Cumuls élèves/parents = comptes créés (l'effectif actif "
                        + "du moment figure dans le bandeau).")
                        .FontSize(7.5f).FontColor(TextSecondary);
                });
            });
        }

        // --- Briques ---

        private static void Kpi(RowDescriptor row, string label, string value, string subtitle)
        {
            row.RelativeItem().PaddingRight(6).Background(SurfaceVariant).Padding(8).Column(c =>
            {
                c.Item().Text(value).Bold().FontSize(12.5f).FontColor(PrimaryHex);
                c.Item().Text(label).SemiBold().FontSize(8).FontColor(TextPrimary);
                c.Item().Text(subtitle).FontSize(7).FontColor(TextSecondary);
            });
        }

        private static void HeaderCell(TableCellDescriptor header, string text, bool alignRight = false)
        {
            var cell = header.Cell().Background(PrimaryHex).PaddingVertical(4).PaddingHorizontal(4);
            (alignRight ? cell.AlignRight() : cell)
                .Text(text).FontColor(Colors.White).SemiBold().FontSize(7.5f);
        }

        // ShowEntire : une ligne de mois n'est jamais coupée entre deux pages (§116c).
        private static IContainer Cell(TableDescriptor table, string background) =>
            table.Cell().Background(background)
                .BorderBottom(1).BorderColor(Border)
                .PaddingVertical(3).PaddingHorizontal(4).ShowEntire();

        private static void Money(TableDescriptor table, string background, long amount) =>
            Cell(table, background).AlignRight().Text(Fmt(amount));

        private static void Header(IContainer container, DateTime generatedAt)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Idara").Bold().FontSize(14).FontColor(PrimaryHex);
                        c.Item().Text("Gestion et paiements des écoles coraniques (daara) — Sénégal")
                            .FontSize(8).FontColor(TextSecondary);
                    });
                    row.ConstantItem(200).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("Rapport investisseur")
                            .Bold().FontSize(12).FontColor(PrimaryHex);
                        c.Item().AlignRight()
                            .Text($"Généré le {generatedAt.Day} {FrMonthsLong[generatedAt.Month - 1]} {generatedAt.Year}")
                            .FontSize(8).FontColor(TextSecondary);
                        c.Item().AlignRight().Text("Document confidentiel")
                            .FontSize(7).FontColor(TextSecondary);
                    });
                });
                col.Item().PaddingTop(5).LineHorizontal(1).LineColor(PrimaryHex);
                col.Item().PaddingBottom(6);
            });
        }

        private static void Footer(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Édité par ").FontColor(TextSecondary).FontSize(7);
                    t.Span("Idara").Bold().FontColor(PrimaryHex).FontSize(7);
                    t.Span(" — Pyranil Solution").FontColor(TextSecondary).FontSize(7);
                });
                row.ConstantItem(70).AlignRight().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(7).FontColor(TextSecondary);
                    t.Span(" / ").FontSize(7).FontColor(TextSecondary);
                    t.TotalPages().FontSize(7).FontColor(TextSecondary);
                });
            });
        }

        private static string Fmt(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            .Replace(",", " ");
    }
}
