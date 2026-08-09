using Idara.API.Common.Utilities;
using Idara.API.DTOs.Export;
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
        byte[] BuildRosterPdf(string schoolName, int year, int month, PaymentRosterResponseDto roster,
            string? schoolNameAr = null);
        byte[] BuildDonorReportPdf(
            string schoolName, string donorName, DateTime? from, DateTime? to,
            IReadOnlyList<(DateTime Date, long Amount)> donations, long total,
            string? schoolNameAr = null);

        /// <summary>
        /// Reçu de virement (F3) : daara → bénéficiaire (salaire / charge). Rendu
        /// en mémoire (byte[]), partageable WhatsApp par le bénéficiaire.
        /// </summary>
        byte[] BuildTransferReceiptPdf(TransferReceiptData data);

        /// <summary>
        /// Rapport financier périodique (F4) : total entrées / sorties / net +
        /// ventilation par catégorie + rappel wallet SenePay + solde global.
        /// </summary>
        byte[] BuildFinanceReportPdf(
            string schoolName, DateTime? from, DateTime? to,
            IReadOnlyList<(string Category, long Amount)> incomeByCategory,
            IReadOnlyList<(string Category, long Amount)> expenseByCategory,
            long totalIncome, long totalExpense,
            long walletAvailableFcfa, long globalBalanceFcfa,
            string? schoolNameAr = null);

        /// <summary>
        /// Export GÉNÉRIQUE d'un historique de transactions (feedback école) :
        /// même document pour tous les écrans qui listent des mouvements —
        /// wallet, paiements reçus, retraits/virements, caisse, paiements du
        /// parent, dons, virements reçus. Table PAYSAGE avec une colonne par
        /// information (§120), les colonnes sans donnée s'effaçant d'elles-mêmes.
        /// </summary>
        /// <param name="ownerName">À qui appartient l'historique (daara, parent, donateur…).</param>
        /// <param name="title">Titre du document (« Historique des paiements reçus »).</param>
        /// <param name="summary">Bandeau de synthèse en tête : (libellé, valeur formatée, en rouge ?).</param>
        /// <param name="counterpartyHeader">
        /// Intitulé de la colonne « qui est en face » : « Beneficiaire » pour des
        /// virements, « Payeur » pour des paiements reçus, « Daara » pour des dons…
        /// Un export MIXTE (wallet) garde le défaut « Contrepartie », la colonne
        /// « Type » suffisant alors à savoir s'il s'agit d'un payeur ou d'un
        /// bénéficiaire. Nommer précisément quand l'export est mono-type.
        /// </param>
        byte[] BuildTransactionsPdf(
            string ownerName, string title, DateTime? from, DateTime? to,
            IReadOnlyList<TransactionPdfRow> rows,
            IReadOnlyList<(string Label, string Value, bool Danger)> summary,
            string counterpartyHeader = "Contrepartie",
            string? ownerNameAr = null);
    }

    public class ExportPdfService : IExportPdfService
    {
        private const string PrimaryHex = "#0B744D";
        private const string TextPrimary = "#0F172A";
        private const string TextSecondary = "#475569";
        private const string Border = "#E2E8F0";
        private const string SurfaceVariant = "#F8FAFC";
        private const string RedHex = "#B91C1C";
        /// <summary>Orange « en cours / en attente » — même sémantique que l'app (§118).</summary>
        private const string WarningHex = "#B45309";

        private static readonly string[] FrMonths =
        {
            "janvier", "février", "mars", "avril", "mai", "juin",
            "juillet", "août", "septembre", "octobre", "novembre", "décembre"
        };

        /// <summary>Date en français long : « 15 juin 2026 » (le Sénégal est à l'heure UTC).</summary>
        private static string FrLongDate(DateTime d) => $"{d.Day} {FrMonths[d.Month - 1]} {d.Year}";

        public byte[] BuildRosterPdf(string schoolName, int year, int month, PaymentRosterResponseDto roster,
            string? schoolNameAr = null)
        {
            var monthLabel = $"{FrMonths[month - 1]} {year}";
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.4f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(9).FontColor(TextPrimary).Bilingual());

                    page.Header().Element(c => Header(c, SchoolDisplayName.From(schoolName, schoolNameAr), "Suivi des paiements", monthLabel));
                    page.Content().Element(c => RosterContent(c, roster));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void RosterContent(IContainer container, PaymentRosterResponseDto roster)
        {
            // Deux groupes seulement (demande école) : ceux qui ont PAYÉ, et ceux
            // EN RETARD (facture du mois non soldée). La facture n'étant générée
            // qu'à l'échéance mensuelle, un impayé = un retard. Les élèves « sans
            // facture » (pas de tarif ce mois, rien à payer) sont exclus.
            var paid = roster.Entries
                .Where(e => e.Status == RosterPaymentStatus.Paid)
                .ToList();
            var overdue = roster.Entries
                .Where(e => e.Status == RosterPaymentStatus.Pending
                         || e.Status == RosterPaymentStatus.Overdue)
                .ToList();

            container.Column(col =>
            {
                col.Item().PaddingBottom(8).Row(row =>
                {
                    Counter(row, "Payé", paid.Count, PrimaryHex);
                    Counter(row, "En retard", overdue.Count, RedHex);
                });

                RosterSection(col, "Payé", paid, PrimaryHex, showPaidAmount: true, topPad: 0);
                RosterSection(col, "En retard", overdue, RedHex, showPaidAmount: false, topPad: 14);
            });
        }

        /// <summary>
        /// Une section du roster : titre + tableau
        /// (Élève / Classe / Parent / Téléphone / Montant [/ Date de paiement]).
        /// Le parent et son numéro servent à relancer directement depuis le PDF
        /// partagé sur WhatsApp ; la date de paiement n'a de sens que pour les payés.
        /// </summary>
        private static void RosterSection(
            ColumnDescriptor col, string title, List<PaymentRosterEntryDto> entries,
            string color, bool showPaidAmount, float topPad)
        {
            col.Item().PaddingTop(topPad).PaddingBottom(4)
                .Text($"{title} ({entries.Count})").SemiBold().FontSize(11).FontColor(color);

            if (entries.Count == 0)
            {
                col.Item().Text("Aucun élève.").Italic().FontSize(9).FontColor(TextSecondary);
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3.0f);   // Élève
                    c.RelativeColumn(2.4f);   // Classe (les noms de classe sont longs : TOUBA NIVEAU AVANCE)
                    c.RelativeColumn(2.9f);   // Parent
                    c.RelativeColumn(2.0f);   // Téléphone
                    c.RelativeColumn(1.6f);   // Montant
                    if (showPaidAmount) c.RelativeColumn(1.3f); // Date de paiement
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Élève", color);
                    HeaderCell(header, "Classe", color);
                    HeaderCell(header, "Parent", color);
                    HeaderCell(header, "Téléphone", color);
                    HeaderCell(header, showPaidAmount ? "Paye" : "Du", color, alignRight: true);
                    if (showPaidAmount) HeaderCell(header, "Date", color, alignRight: true);
                });

                foreach (var e in entries)
                {
                    var name = $"{e.StudentFirstName} {e.StudentLastName}".Trim();
                    var amount = showPaidAmount ? e.AmountPaidFcfa : e.AmountDueFcfa;

                    BodyCell(table).Text(name).FontSize(8.5f);
                    BodyCell(table).Text(e.ClassName ?? "-").FontSize(8);
                    BodyCell(table).Text(string.IsNullOrWhiteSpace(e.GuardianFullName) ? "-" : e.GuardianFullName)
                        .FontSize(8);
                    BodyCell(table).Text(FrPhone(e.GuardianPhone)).FontSize(8);
                    BodyCell(table).AlignRight().Text($"{amount:N0}").FontSize(8);
                    if (showPaidAmount)
                        BodyCell(table).AlignRight()
                            .Text(e.PaidAt.HasValue ? $"{e.PaidAt.Value:dd/MM}" : "-").FontSize(8);
                }
            });
        }

        private static void HeaderCell(TableCellDescriptor header, string text, string color, bool alignRight = false)
        {
            var cell = header.Cell().Background(color).PaddingVertical(4).PaddingHorizontal(4);
            (alignRight ? cell.AlignRight() : cell)
                .Text(text).FontColor(Colors.White).SemiBold().FontSize(8.5f);
        }

        // ShowEntire : une ligne d'élève n'est jamais coupée en deux par un saut
        // de page (sinon on lit « Mouhamadou » en bas d'une page et « Mbacke » en
        // haut de la suivante).
        private static IContainer BodyCell(TableDescriptor table) =>
            table.Cell().BorderBottom(1).BorderColor(Border)
                .PaddingVertical(3).PaddingHorizontal(4).ShowEntire();

        /// <summary>
        /// Numéro lisible : « +221771234567 » → « 77 123 45 67 ». Délègue à
        /// <see cref="Common.Utilities.SenegalPhone.ToDisplay"/>, source unique de
        /// ce formatage (la logique était dupliquée avec le service d'alerte).
        /// </summary>
        private static string FrPhone(string? phone) =>
            Common.Utilities.SenegalPhone.ToDisplay(phone, fallback: "-");

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
        /// <summary>
        /// Tuile du bandeau de synthèse. ⚠️ Ajoute « FCFA » : n'y mettre QUE des
        /// MONTANTS. Un compteur (« 3 Transactions ») s'y affichait « 3 FCFA » —
        /// et faisait doublon avec le total imprimé sous le tableau.
        /// </summary>
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
            IReadOnlyList<(DateTime Date, long Amount)> donations, long total,
            string? schoolNameAr = null)
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
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary).Bilingual());

                    page.Header().Element(c => Header(c, SchoolDisplayName.From(schoolName, schoolNameAr), "Rapport de dons", period));
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
                    col.Item().PaddingTop(10).Text("Aucun don sur la période.").FontColor(TextSecondary).Italic();
            });
        }

        public byte[] BuildTransferReceiptPdf(TransferReceiptData data)
        {
            var name = SchoolDisplayName.From(data.SchoolName, data.SchoolNameAr);
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary).Bilingual());

                    page.Header().Element(c => Header(c, name, "Reçu de virement", $"N° {data.TransferId:D6}"));
                    page.Content().Element(c => TransferContent(c, data));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void TransferContent(IContainer container, TransferReceiptData d)
        {
            container.Column(col =>
            {
                col.Item().Background(SurfaceVariant).Padding(10).Column(c =>
                {
                    InfoLine(c, "Bénéficiaire", d.BeneficiaryName, bold: true);
                    // Le numéro dit À QUI l'argent est parti : c'est la première
                    // chose qu'on vérifie quand un enseignant conteste un versement.
                    if (!string.IsNullOrWhiteSpace(d.BeneficiaryPhone))
                        InfoLine(c, "Téléphone", d.BeneficiaryPhone!);
                    InfoLine(c, "Nature", d.CategoryLabel);
                    InfoLine(c, "Date", $"{d.Date:dd/MM/yyyy}");
                    // Motif : le bénéficiaire doit savoir ce que ce versement paie.
                    if (!string.IsNullOrWhiteSpace(d.Motif))
                        InfoLine(c, "Motif", d.Motif!);
                });

                col.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                    });

                    table.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6)
                        .Text("Montant reçu").FontColor(Colors.White).SemiBold();
                    table.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                        .Text($"{d.AmountFcfa:N0} FCFA").FontColor(Colors.White).SemiBold();

                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6)
                        .Text("Moyen");
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                        .Text(d.OperatorLabel);

                    table.Cell().PaddingVertical(6).PaddingHorizontal(6).Text("Statut").Bold();
                    table.Cell().PaddingVertical(6).PaddingHorizontal(6).AlignRight()
                        .Text(d.StatusLabel).Bold().FontColor(StatusColor(d.StatusLabel));
                });

                col.Item().PaddingTop(10).Element(c => PdfBlocks.References(
                    c, d.Reference, d.ProviderReference, TextSecondary, Border));

                col.Item().PaddingTop(12).Text(
                    "Document généré électroniquement, sans signature. Ce reçu atteste du virement reçu.")
                    .FontSize(8).FontColor(TextSecondary);
            });
        }

        /// <summary>Une ligne « libellé : valeur » du bloc d'identité d'un reçu.</summary>
        private static void InfoLine(ColumnDescriptor col, string label, string value, bool bold = false)
        {
            col.Item().Text(t =>
            {
                t.Span($"{label} : ").SemiBold().FontColor(TextSecondary);
                var span = t.Span(value);
                if (bold) span.Bold();
            });
        }

        public byte[] BuildFinanceReportPdf(
            string schoolName, DateTime? from, DateTime? to,
            IReadOnlyList<(string Category, long Amount)> incomeByCategory,
            IReadOnlyList<(string Category, long Amount)> expenseByCategory,
            long totalIncome, long totalExpense,
            long walletAvailableFcfa, long globalBalanceFcfa,
            string? schoolNameAr = null)
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
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary).Bilingual());

                    page.Header().Element(c => Header(c, SchoolDisplayName.From(schoolName, schoolNameAr), "Rapport financier", period));
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
                    MoneyCounter(row, "Entrées", $"{totalIncome:N0}", PrimaryHex);
                    MoneyCounter(row, "Sorties", $"{totalExpense:N0}", RedHex);
                    MoneyCounter(row, "Net", $"{net:N0}", net >= 0 ? PrimaryHex : RedHex);
                });

                CategoryTable(col, "Entrées par catégorie", incomeByCategory, totalIncome, PrimaryHex);
                col.Item().PaddingTop(10);
                CategoryTable(col, "Sorties par catégorie", expenseByCategory, totalExpense, RedHex);

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
                        .Text("Catégorie").FontColor(Colors.White).SemiBold();
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

        // ============================================================
        // ===== Historique de transactions (export générique) =====
        // ============================================================

        public byte[] BuildTransactionsPdf(
            string ownerName, string title, DateTime? from, DateTime? to,
            IReadOnlyList<TransactionPdfRow> rows,
            IReadOnlyList<(string Label, string Value, bool Danger)> summary,
            string counterpartyHeader = "Contrepartie",
            string? ownerNameAr = null)
        {
            var period = from.HasValue || to.HasValue
                ? (from.HasValue ? FrLongDate(from.Value) : "origine") + " -> " +
                  (to.HasValue ? FrLongDate(to.Value) : "aujourd'hui")
                : "Historique complet";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // PAYSAGE : une colonne par information (cf. TransactionPdfRow).
                    // En portrait, 7 colonnes deviendraient illisibles et la
                    // référence SenePay ne tiendrait pas.
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(9).FontColor(TextPrimary).Bilingual());

                    page.Header().Element(c => Header(c, SchoolDisplayName.From(ownerName, ownerNameAr), title, period));
                    page.Content().Element(c => TransactionsContent(c, rows, summary, counterpartyHeader));
                    page.Footer().Element(Footer);
                });
            });
            return doc.GeneratePdf();
        }

        private static void TransactionsContent(
            IContainer container, IReadOnlyList<TransactionPdfRow> rows,
            IReadOnlyList<(string Label, string Value, bool Danger)> summary,
            string counterpartyHeader)
        {
            // Signe explicite uniquement si la liste mélange entrées ET sorties
            // (un historique de dons n'a que des « + » : le signe serait du bruit).
            var mixed = rows.Any(r => r.AmountFcfa > 0) && rows.Any(r => r.AmountFcfa < 0);

            container.Column(col =>
            {
                if (summary.Count > 0)
                {
                    col.Item().PaddingBottom(8).Row(row =>
                    {
                        foreach (var (label, value, danger) in summary)
                            MoneyCounter(row, label, value, danger ? RedHex : PrimaryHex);
                    });
                }

                if (rows.Count == 0)
                {
                    col.Item().PaddingTop(6)
                        .Text("Aucune transaction sur cette periode.").Italic().FontColor(TextSecondary);
                    return;
                }

                // Colonnes affichées seulement si au moins une ligne les remplit :
                // un export de dons n'a pas de « Solde », la caisse n'a pas de
                // « Référence » — pas de colonne vide qui vole de la largeur aux
                // autres.
                var hasMethod = rows.Any(r => !string.IsNullOrWhiteSpace(r.Method));
                var hasReference = rows.Any(r => !string.IsNullOrWhiteSpace(r.Reference));
                var hasBalance = rows.Any(r => !string.IsNullOrWhiteSpace(r.Balance));
                var hasNote = rows.Any(r => !string.IsNullOrWhiteSpace(r.Note));
                var hasPhone = rows.Any(r => !string.IsNullOrWhiteSpace(r.Phone));

                col.Item().Table(table =>
                {
                    // UNE COLONNE PAR INFORMATION, en paysage : le document se lit
                    // et se trie comme un tableur. Les largeurs sont calibrées pour
                    // que la référence (bloc de ~68 caractères sans espace) tienne
                    // dans sa colonne en se repliant, sans jamais déborder.
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.4f);                     // Date
                        c.RelativeColumn(1.6f);                     // Type
                        c.RelativeColumn(1.1f);                     // Statut
                        c.RelativeColumn(2.4f);                     // Contrepartie
                        if (hasPhone) c.RelativeColumn(1.3f);       // Numéro
                        if (hasNote) c.RelativeColumn(2.2f);        // Motif
                        if (hasMethod) c.RelativeColumn(1.1f);      // Moyen
                        if (hasReference) c.RelativeColumn(2.1f);   // Référence
                        if (hasBalance) c.RelativeColumn(1.3f);     // Solde après
                        c.RelativeColumn(1.5f);                     // Montant
                    });

                    // En-tête répété automatiquement en haut de chaque page.
                    table.Header(header =>
                    {
                        HeaderCell(header, "Date", PrimaryHex);
                        HeaderCell(header, "Type", PrimaryHex);
                        HeaderCell(header, "Statut", PrimaryHex);
                        // Intitulé adapté à l'export : « Beneficiaire », « Payeur »,
                        // « Daara »… et seulement « Contrepartie » quand l'export
                        // mélange les sens (la colonne Type lève alors le doute).
                        HeaderCell(header, counterpartyHeader, PrimaryHex);
                        if (hasPhone) HeaderCell(header, "Numéro", PrimaryHex);
                        if (hasNote) HeaderCell(header, "Motif", PrimaryHex);
                        if (hasMethod) HeaderCell(header, "Moyen", PrimaryHex);
                        if (hasReference) HeaderCell(header, "Référence", PrimaryHex);
                        if (hasBalance) HeaderCell(header, "Solde après", PrimaryHex, alignRight: true);
                        HeaderCell(header, "Montant (FCFA)", PrimaryHex, alignRight: true);
                    });

                    var index = 0;
                    foreach (var r in rows)
                    {
                        // Alternance de fond : l'œil suit la ligne jusqu'au montant.
                        var bg = index++ % 2 == 1 ? SurfaceVariant : "#FFFFFF";

                        TxCell(table, bg).Column(c =>
                        {
                            c.Item().Text($"{r.Date:dd/MM/yyyy}").FontSize(8);
                            c.Item().Text($"{r.Date:HH:mm}").FontSize(7).FontColor(TextSecondary);
                        });

                        TxCell(table, bg).Text(r.Title).SemiBold().FontSize(8);

                        TxCell(table, bg).Text(r.Status ?? "-").FontSize(8)
                            .FontColor(StatusColor(r.Status));

                        TxCell(table, bg)
                            .Text(string.IsNullOrWhiteSpace(r.Subtitle) ? "-" : r.Subtitle)
                            .FontSize(8);

                        if (hasPhone)
                            TxCell(table, bg).Text(r.Phone ?? "-").FontSize(8)
                                .FontColor(TextSecondary);

                        if (hasNote)
                            // Motif saisi par l'école : texte libre, souvent long, en
                            // italique pour le distinguer des données structurées.
                            TxCell(table, bg).Text(r.Note ?? "-").FontSize(7.5f)
                                .Italic().FontColor(TextSecondary);

                        if (hasMethod)
                            TxCell(table, bg).Text(r.Method ?? "-").FontSize(7.5f)
                                .FontColor(TextSecondary);

                        if (hasReference)
                            // Bloc sans espace : QuestPDF le coupe seul en bout de ligne
                            // (WrapAnywhere est obsolète depuis 2024.3) → il se replie
                            // sur 2-3 lignes dans sa colonne au lieu de déborder.
                            TxCell(table, bg).Text(r.Reference ?? "-").FontSize(6.5f)
                                .FontColor(TextSecondary);

                        if (hasBalance)
                            TxCell(table, bg).AlignRight().Text(r.Balance ?? "-").FontSize(7.5f)
                                .FontColor(TextSecondary);

                        var abs = Math.Abs(r.AmountFcfa);
                        var text = mixed
                            ? (r.AmountFcfa < 0 ? $"-{abs:N0}" : $"+{abs:N0}")
                            : $"{abs:N0}";
                        TxCell(table, bg).AlignRight().Text(text).SemiBold().FontSize(8.5f)
                            .FontColor(r.AmountFcfa < 0 ? RedHex : PrimaryHex);
                    }
                });

                col.Item().PaddingTop(8)
                    .Text($"{rows.Count} transaction(s).").FontSize(8).FontColor(TextSecondary);
            });
        }

        /// <summary>
        /// Couleur du statut dans le tableau : la même sémantique que dans l'app
        /// (gotcha §118) — réussi vert, en cours orange, échec rouge. Le document
        /// imprimé et l'écran ne doivent jamais raconter deux histoires.
        /// </summary>
        private static string StatusColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return TextSecondary;
            var s = status.ToLowerInvariant();
            if (s.Contains("echou") || s.Contains("échou") || s.Contains("annul") || s.Contains("expir"))
                return RedHex;
            if (s.Contains("cours") || s.Contains("attente") || s.Contains("verif") || s.Contains("vérif"))
                return WarningHex;
            return PrimaryHex;
        }

        // ShowEntire : une transaction reste d'un seul bloc — jamais sa moitié en
        // bas d'une page et le reste sur la suivante.
        private static IContainer TxCell(TableDescriptor table, string background) =>
            table.Cell().Background(background)
                .BorderBottom(1).BorderColor(Border)
                .PaddingVertical(4).PaddingHorizontal(5).ShowEntire();

        /// <summary>
        /// En-tête commun à tous les documents. Le nom du daara y est BILINGUE
        /// quand les deux écritures sont renseignées : nom français en titre, nom
        /// arabe juste en dessous, sur sa propre ligne et dans son propre sens de
        /// lecture. Jamais concaténés — c'est le bricolage « Nom (الاسم) » qu'on
        /// remplace, et il produisait cinq lignes de carrés faute de police.
        /// </summary>
        private static void Header(IContainer container, SchoolDisplayName owner, string title, string subtitle)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(owner.Primary()).Bold().FontSize(13).FontColor(TextPrimary);
                        // Le second nom est SECONDAIRE : un cran plus petit, un cran
                        // moins contrasté. Deux titres de même poids se disputeraient
                        // l'attention (règle R3 de la proposition validée).
                        //
                        // ⚠️ AlignRight n'est PAS décoratif. Sans lui, un nom arabe
                        // trop long pour une ligne se replie collé à GAUCHE : la
                        // suite d'une phrase arabe se retrouve alors du mauvais côté
                        // et la lecture décroche. Vérifié au rendu sur le nom réel
                        // d'un daara, qui tient sur deux lignes.
                        if (owner.Secondary is string secondary)
                            c.Item().AlignRight().Text(secondary).SemiBold().FontSize(11.5f)
                                .FontColor(TextSecondary).DirectionFromRightToLeft();
                        c.Item().Text("via Idara").FontSize(8).FontColor(TextSecondary);
                    });
                    // 155 et non 190 : la colonne de droite n'a besoin que de la
                    // largeur de « Généré le 8 août 2026 ». Les 35 points rendus au
                    // bloc d'identité lui épargnent une ligne de repli sur les noms
                    // longs, qui sont la règle chez les daara.
                    row.ConstantItem(155).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text(title).Bold().FontSize(12).FontColor(PrimaryHex);
                        c.Item().AlignRight().Text(subtitle).FontSize(9).FontColor(TextSecondary);
                        c.Item().AlignRight().Text($"Généré le {FrLongDate(DateTime.UtcNow)}")
                            .FontSize(8).FontColor(TextSecondary);
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
                    t.Span("Édité par ").FontColor(TextSecondary).FontSize(7);
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
    }
}
