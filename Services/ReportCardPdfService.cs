using Idara.API.Common.Utilities;
using Idara.API.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Idara.API.Services
{
    public class ReportCardPdfService : IReportCardPdfService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ReportCardPdfService> _logger;
        private readonly IPdfFileNamer _namer;

        // Palette alignée sur AppColors côté Flutter (cohérence UI/PDF).
        private const string PrimaryHex = "#16A34A";   // vert Idara
        private const string TextPrimary = "#0F172A";
        private const string TextSecondary = "#475569";
        private const string Border = "#E2E8F0";
        private const string SurfaceVariant = "#F8FAFC";

        public ReportCardPdfService(
            IWebHostEnvironment env,
            ILogger<ReportCardPdfService> logger,
            IPdfFileNamer namer)
        {
            _env = env;
            _logger = logger;
            _namer = namer;
        }

        public async Task<string> GenerateAsync(ReportCard card, School school)
        {
            var folder = Path.Combine(_env.WebRootPath, "uploads", "bulletins");
            Directory.CreateDirectory(folder);

            // Nom déterministe → un upsert remplace l'ancien fichier. Le suffixe
            // HMAC le rend indevinable : sans lui, les notes d'un enfant étaient
            // énumérables (école, élève et période sont de petits entiers).
            var fileName = _namer.Build(
                "bulletin", $"{card.SchoolId}-{card.StudentId}-{card.AcademicPeriodId}");
            var fullPath = Path.Combine(folder, fileName);

            try
            {
                var doc = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary));

                        page.Header().Element(c => ComposeHeader(c, card, school));
                        page.Content().Element(c => ComposeContent(c, card));
                        page.Footer().Element(ComposeFooter);
                    });
                });

                // QuestPDF expose GeneratePdf(string path) synchrone.
                // On le wrappe en Task.Run pour ne pas bloquer le thread requête.
                await Task.Run(() => doc.GeneratePdf(fullPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec génération PDF bulletin {File}", fullPath);
                throw;
            }

            return $"/uploads/bulletins/{fileName}";
        }

        // ----- Composition QuestPDF -----

        private static void ComposeHeader(IContainer container, ReportCard card, School school)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(school.Name ?? "École").Bold().FontSize(14).FontColor(PrimaryHex);
                        if (!string.IsNullOrWhiteSpace(school.Address))
                            c.Item().Text(school.Address).FontSize(9).FontColor(TextSecondary);
                        if (!string.IsNullOrWhiteSpace(school.PhoneNumber))
                            c.Item().Text(school.PhoneNumber).FontSize(9).FontColor(TextSecondary);
                    });
                    row.ConstantItem(180).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("BULLETIN SCOLAIRE").Bold().FontSize(13).FontColor(PrimaryHex);
                        c.Item().AlignRight().Text($"Période : {card.AcademicPeriod?.Name ?? "—"}").FontSize(9).FontColor(TextSecondary);
                        c.Item().AlignRight().Text($"Édité le {card.GeneratedAt:dd/MM/yyyy}").FontSize(9).FontColor(TextSecondary);
                    });
                });
                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(PrimaryHex);
                col.Item().PaddingBottom(10);
            });
        }

        private static void ComposeContent(IContainer container, ReportCard card)
        {
            container.Column(col =>
            {
                // Carte élève
                col.Item().Background(SurfaceVariant).Padding(12).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Élève : ").SemiBold().FontColor(TextSecondary);
                            t.Span(card.Student != null
                                ? $"{card.Student.FirstName} {card.Student.LastName}"
                                : "—").Bold();
                        });
                        if (!string.IsNullOrWhiteSpace(card.Student?.StudentNumber))
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Matricule : ").SemiBold().FontColor(TextSecondary);
                                t.Span(card.Student!.StudentNumber!);
                            });
                        }
                        c.Item().Text(t =>
                        {
                            t.Span("Classe : ").SemiBold().FontColor(TextSecondary);
                            t.Span(card.Class?.Name ?? "—");
                        });
                    });
                    row.ConstantItem(170).Column(c =>
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Moyenne générale : ").SemiBold().FontColor(TextSecondary);
                            t.Span($"{card.GeneralAverage:0.00} / 20").Bold().FontColor(PrimaryHex);
                        });
                        // Écoles franco-arabes : les deux cursus, chacun sa
                        // moyenne. Affiché UNIQUEMENT si l'école enseigne
                        // réellement les deux domaines — sur un daara, ce serait
                        // répéter la moyenne générale sous un autre nom.
                        if (ReportCardDomains.ShowBothDomains(card.ArabicAverage, card.GeneralSubjectsAverage))
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Arabe / religieux : ").SemiBold().FontColor(TextSecondary);
                                t.Span($"{card.ArabicAverage:0.00} / 20");
                            });
                            c.Item().Text(t =>
                            {
                                t.Span("Français / général : ").SemiBold().FontColor(TextSecondary);
                                t.Span($"{card.GeneralSubjectsAverage:0.00} / 20");
                            });
                        }
                        if (!string.IsNullOrWhiteSpace(card.Mention))
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Mention : ").SemiBold().FontColor(TextSecondary);
                                t.Span(card.Mention!);
                            });
                        }
                        if (card.Rank.HasValue && card.TotalStudents.HasValue)
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Rang : ").SemiBold().FontColor(TextSecondary);
                                t.Span($"{card.Rank} / {card.TotalStudents}");
                            });
                        }
                    });
                });

                col.Item().PaddingVertical(14).Element(e => ComposeGradesTable(e, card));

                if (!string.IsNullOrWhiteSpace(card.Appreciation))
                {
                    col.Item().Border(1).BorderColor(Border).Padding(10).Column(c =>
                    {
                        c.Item().Text("Appréciation générale").SemiBold().FontColor(PrimaryHex);
                        c.Item().PaddingTop(4).Text(card.Appreciation!);
                    });
                }
            });
        }

        private static void ComposeGradesTable(IContainer container, ReportCard card)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4); // Matière
                    c.RelativeColumn(1); // Coef
                    c.RelativeColumn(2); // Moyenne /20
                    c.RelativeColumn(1); // Rang
                    c.RelativeColumn(4); // Appréciation
                });

                // Header
                table.Header(h =>
                {
                    h.Cell().Background(PrimaryHex).Padding(6).Text("Matière").FontColor(Colors.White).SemiBold();
                    h.Cell().Background(PrimaryHex).Padding(6).AlignCenter().Text("Coef.").FontColor(Colors.White).SemiBold();
                    h.Cell().Background(PrimaryHex).Padding(6).AlignCenter().Text("Moyenne /20").FontColor(Colors.White).SemiBold();
                    h.Cell().Background(PrimaryHex).Padding(6).AlignCenter().Text("Rang").FontColor(Colors.White).SemiBold();
                    h.Cell().Background(PrimaryHex).Padding(6).Text("Appréciation").FontColor(Colors.White).SemiBold();
                });

                if (card.Lines.Count == 0)
                {
                    table.Cell().ColumnSpan(5).Padding(8).AlignCenter()
                        .Text("Aucune note enregistrée pour cette période.")
                        .Italic().FontColor(TextSecondary);
                }
                else
                {
                    foreach (var line in card.Lines)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Border).Padding(6)
                            .Text(string.IsNullOrEmpty(line.SubjectName) ? (line.Subject?.Name ?? "—") : line.SubjectName);
                        table.Cell().BorderBottom(1).BorderColor(Border).Padding(6).AlignCenter()
                            .Text($"{line.Coefficient:0.##}");
                        table.Cell().BorderBottom(1).BorderColor(Border).Padding(6).AlignCenter()
                            .Text($"{line.Average:0.00}");
                        table.Cell().BorderBottom(1).BorderColor(Border).Padding(6).AlignCenter()
                            .Text(line.RankInClass.HasValue ? $"{line.RankInClass}" : "—");
                        table.Cell().BorderBottom(1).BorderColor(Border).Padding(6)
                            .Text(line.Appreciation ?? "—").FontColor(TextSecondary);
                    }
                }
            });
        }

        private static void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(t =>
            {
                t.Span("Document généré automatiquement par ").FontColor(TextSecondary).FontSize(8);
                t.Span("Idara").Bold().FontColor(PrimaryHex).FontSize(8);
                t.Span($" — {DateTime.UtcNow:dd/MM/yyyy HH:mm}").FontColor(TextSecondary).FontSize(8);
            });
        }
    }
}
