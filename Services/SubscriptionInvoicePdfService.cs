using Idara.API.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Idara.API.Services
{
    /// <summary>
    /// Génère les PDF des factures d'abonnement plateforme (école → Idara).
    /// Format A5 portrait, 1 page. Chemin déterministe
    /// <c>wwwroot/uploads/subscription-invoices/sub-invoice-{id}.pdf</c> (upsert).
    /// Calqué sur <see cref="ReceiptPdfService"/>.
    /// </summary>
    public class SubscriptionInvoicePdfService : ISubscriptionInvoicePdfService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SubscriptionInvoicePdfService> _logger;

        private const string PrimaryHex = "#0B744D";
        private const string TextPrimary = "#0F172A";
        private const string TextSecondary = "#475569";
        private const string Border = "#E2E8F0";
        private const string SurfaceVariant = "#F8FAFC";

        public SubscriptionInvoicePdfService(IWebHostEnvironment env, ILogger<SubscriptionInvoicePdfService> logger)
        {
            _env = env;
            _logger = logger;
        }

        public async Task<string> GenerateAsync(SubscriptionInvoice invoice, School school, string? planName)
        {
            var folder = Path.Combine(_env.WebRootPath, "uploads", "subscription-invoices");
            Directory.CreateDirectory(folder);

            var fileName = $"sub-invoice-{invoice.Id}.pdf";
            var fullPath = Path.Combine(folder, fileName);

            try
            {
                var doc = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A5);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary));

                        page.Header().Element(c => ComposeHeader(c, school, invoice));
                        page.Content().Element(c => ComposeContent(c, invoice, planName));
                        page.Footer().Element(ComposeFooter);
                    });
                });

                await Task.Run(() => doc.GeneratePdf(fullPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec génération PDF facture abo {File}", fullPath);
                throw;
            }

            return $"/uploads/subscription-invoices/{fileName}";
        }

        private static void ComposeHeader(IContainer container, School school, SubscriptionInvoice invoice)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Idara").Bold().FontSize(15).FontColor(PrimaryHex);
                        c.Item().Text("Plateforme de gestion d'écoles").FontSize(8).FontColor(TextSecondary);
                    });
                    row.ConstantItem(160).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("FACTURE D'ABONNEMENT").Bold().FontSize(11).FontColor(PrimaryHex);
                        c.Item().AlignRight().Text($"N° {invoice.Id:D6}").FontSize(9).FontColor(TextSecondary);
                        var d = invoice.PaidAt ?? invoice.IssuedAt;
                        c.Item().AlignRight().Text($"Du {d:dd/MM/yyyy}").FontSize(8).FontColor(TextSecondary);
                    });
                });
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(PrimaryHex);
                col.Item().PaddingBottom(8);
            });
        }

        private static void ComposeContent(IContainer container, SubscriptionInvoice invoice, string? planName)
        {
            container.Column(col =>
            {
                col.Item().Background(SurfaceVariant).Padding(10).Column(c =>
                {
                    c.Item().Text(t =>
                    {
                        t.Span("École : ").SemiBold().FontColor(TextSecondary);
                        t.Span(invoice.SchoolId.ToString());
                    });
                    if (!string.IsNullOrWhiteSpace(planName))
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Plan : ").SemiBold().FontColor(TextSecondary);
                            t.Span(planName!);
                        });
                    }
                    c.Item().Text(t =>
                    {
                        t.Span("Période : ").SemiBold().FontColor(TextSecondary);
                        t.Span($"{invoice.PeriodStart:dd/MM/yyyy} → {invoice.PeriodEnd:dd/MM/yyyy}");
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
                        .Text("Montant de l'abonnement").FontColor(Colors.White).SemiBold();
                    table.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                        .Text($"{invoice.AmountFcfa:N0} FCFA").FontColor(Colors.White).SemiBold();

                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6)
                        .Text("Mode de règlement");
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                        .Text("Prélèvement wallet").FontSize(9);

                    table.Cell().PaddingVertical(6).PaddingHorizontal(6).Text("Statut").Bold();
                    var statusCell = table.Cell().PaddingVertical(6).PaddingHorizontal(6).AlignRight()
                        .Text(FrenchStatus(invoice.Status)).Bold();
                    if (invoice.Status == Enums.SubscriptionInvoiceStatus.Paid)
                        statusCell.FontColor(PrimaryHex);
                });

                col.Item().PaddingTop(14).Text(
                    "Document généré électroniquement, sans signature.")
                    .FontSize(8).FontColor(TextSecondary);
            });
        }

        private static void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(t =>
            {
                t.Span("Édité par ").FontColor(TextSecondary).FontSize(7);
                t.Span("Idara").Bold().FontColor(PrimaryHex).FontSize(7);
                t.Span($" — {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC").FontColor(TextSecondary).FontSize(7);
            });
        }

        private static string FrenchStatus(Enums.SubscriptionInvoiceStatus s) => s switch
        {
            Enums.SubscriptionInvoiceStatus.Paid => "Payé",
            Enums.SubscriptionInvoiceStatus.Pending => "En attente",
            Enums.SubscriptionInvoiceStatus.Failed => "Échec",
            Enums.SubscriptionInvoiceStatus.Cancelled => "Annulé",
            _ => s.ToString()
        };
    }
}
