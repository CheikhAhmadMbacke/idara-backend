using Idara.API.Common.Utilities;
using Idara.API.Enums;
using Idara.API.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Idara.API.Services
{
    /// <summary>
    /// Génère les reçus PDF des paiements parent → école. Format A5 portrait,
    /// minimaliste (1 page), prêt à imprimer ou être affiché dans le
    /// DocumentViewer Flutter.
    ///
    /// Convention chemin :
    /// <c>wwwroot/uploads/receipts/receipt-{paymentId}-{suffixeHMAC}.pdf</c>
    /// (cf. <see cref="IPdfFileNamer"/>). Nom toujours déterministe → un upsert
    /// (génération à la fin du webhook, puis re-génération depuis l'endpoint
    /// download) écrase l'ancien fichier sans accumulation ; mais le suffixe le
    /// rend indevinable, donc non énumérable si jamais le fichier se retrouvait
    /// servi en direct.
    /// </summary>
    public class ReceiptPdfService : IReceiptPdfService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ReceiptPdfService> _logger;
        private readonly IPdfFileNamer _namer;

        // Palette alignée sur AppColors côté Flutter.
        private const string PrimaryHex = "#16A34A";
        private const string TextPrimary = "#0F172A";
        private const string TextSecondary = "#475569";
        private const string Border = "#E2E8F0";
        private const string SurfaceVariant = "#F8FAFC";

        public ReceiptPdfService(
            IWebHostEnvironment env,
            ILogger<ReceiptPdfService> logger,
            IPdfFileNamer namer)
        {
            _env = env;
            _logger = logger;
            _namer = namer;
        }

        /// <summary>
        /// Nom de fichier attendu pour le reçu d'un paiement. Exposé (statique via
        /// l'interface) parce que plusieurs endpoints doivent recalculer ce chemin
        /// quand <c>Payment.ReceiptPdfPath</c> n'a jamais été persisté.
        /// </summary>
        public string RelativePathFor(int paymentId) =>
            $"/uploads/receipts/{_namer.Build("receipt", paymentId.ToString())}";

        /// <summary>Chemin de l'aperçu PNG dérivé du chemin du PDF (même nom, `.png`).</summary>
        public static string PreviewPathOf(string pdfPath) => Path.ChangeExtension(pdfPath, ".png");

        public async Task<string> GenerateAsync(
            Payment payment, School school, Student? student, Invoice? invoice, User? donor = null,
            IReadOnlyList<ReceiptConsolidatedLine>? consolidatedLines = null, User? payer = null)
        {
            // Contrepartie du paiement : le donateur pour un don, le parent payeur
            // sinon. Son numéro figure sur le reçu comme celui du bénéficiaire
            // figure sur un reçu de virement — c'est ce qui permet de rapprocher
            // un encaissement d'une personne quand une famille conteste.
            var counterparty = donor ?? payer;
            // Un don par lien public n'a AUCUN compte derrière lui : le numéro
            // vient alors de ce que le donateur a déclaré au formulaire.
            var payerPhone = SenegalPhone.ToDisplay(
                counterparty?.PhoneNumber ?? payment.DonorPhone, fallback: string.Empty);
            var folder = Path.Combine(_env.WebRootPath, "uploads", "receipts");
            Directory.CreateDirectory(folder);

            var fileName = _namer.Build("receipt", payment.Id.ToString());
            var fullPath = Path.Combine(folder, fileName);

            var isDonation = payment.Purpose == PaymentPurpose.Donation;
            var logoBytes = LoadLogoBytes(school.LogoUrl);

            try
            {
                var doc = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A5);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextPrimary).Bilingual());

                        page.Header().Element(c => ComposeHeader(c, school, payment, isDonation, logoBytes));
                        page.Content().Element(c => ComposeContent(c, payment, student, invoice, donor, isDonation, consolidatedLines, payerPhone));
                        page.Footer().Element(c => ComposeFooter(c, isDonation));
                    });
                });

                await Task.Run(() => doc.GeneratePdf(fullPath));

                // Aperçu PNG (1ʳᵉ page) pour la page de résultat : Chrome Android
                // ne rend PAS un PDF dans une <iframe> (il affiche un bloc noir
                // « Ouvrir »). Best-effort : un échec d'image ne doit jamais faire
                // perdre le PDF déjà écrit.
                try
                {
                    var pngPath = PreviewPathOf(fullPath);
                    await Task.Run(() =>
                    {
                        var images = doc.GenerateImages(new ImageGenerationSettings
                        {
                            ImageFormat = ImageFormat.Png,
                            RasterDpi = 144,
                        });
                        var first = images.FirstOrDefault();
                        if (first != null) File.WriteAllBytes(pngPath, first);
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Aperçu PNG du reçu non généré pour {File} (PDF intact)", fullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec génération PDF reçu {File}", fullPath);
                throw;
            }

            return $"/uploads/receipts/{fileName}";
        }

        // ===== Composition QuestPDF =====

        /// <summary>
        /// Charge les bytes du logo du daara depuis le disque (best-effort, défense
        /// path-traversal). Renvoie null si absent/hors wwwroot/illisible → le reçu
        /// s'affiche alors sans logo (comportement inchangé).
        /// </summary>
        private byte[]? LoadLogoBytes(string? logoUrl)
        {
            if (string.IsNullOrWhiteSpace(logoUrl)) return null;
            try
            {
                var webRoot = Path.GetFullPath(_env.WebRootPath);
                var full = Path.GetFullPath(Path.Combine(
                    webRoot, logoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(webRoot, StringComparison.Ordinal) || !File.Exists(full))
                    return null;
                return File.ReadAllBytes(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Logo daara illisible pour le reçu ({Url}) — reçu sans logo", logoUrl);
                return null;
            }
        }

        private static void ComposeHeader(IContainer container, School school, Payment payment, bool isDonation, byte[]? logoBytes)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Row(left =>
                    {
                        if (logoBytes != null)
                            left.ConstantItem(42).PaddingRight(8).AlignMiddle()
                                .Height(42).Image(logoBytes).FitArea();
                        left.RelativeItem().Column(c =>
                        {
                            // Nom BILINGUE : français en titre, arabe juste en dessous,
                            // chacun sur sa ligne et dans son propre sens de lecture
                            // (jamais concaténés — cf. SchoolDisplayName).
                            var name = SchoolDisplayName.From(school);
                            c.Item().Text(name.Primary("École")).Bold().FontSize(13).FontColor(PrimaryHex);
                            // AlignRight : sans lui, un nom arabe qui déborde d'une
                            // ligne se replie collé à gauche et se lit à l'envers.
                            if (name.Secondary is string secondary)
                                c.Item().AlignRight().Text(secondary).SemiBold().FontSize(11.5f)
                                    .FontColor(TextSecondary).DirectionFromRightToLeft();
                            if (!string.IsNullOrWhiteSpace(school.Address))
                                c.Item().Text(school.Address).FontSize(8).FontColor(TextSecondary);
                            if (!string.IsNullOrWhiteSpace(school.PhoneNumber))
                                c.Item().Text(school.PhoneNumber).FontSize(8).FontColor(TextSecondary);
                        });
                    });
                    row.ConstantItem(150).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text(isDonation ? "REÇU DE DON" : "REÇU DE PAIEMENT")
                            .Bold().FontSize(11).FontColor(PrimaryHex);
                        c.Item().AlignRight().Text($"N° {payment.Id:D6}").FontSize(9).FontColor(TextSecondary);
                        var paidAt = payment.PaidAt ?? payment.InitiatedAt;
                        c.Item().AlignRight().Text($"Du {paidAt:dd/MM/yyyy HH:mm}").FontSize(8).FontColor(TextSecondary);
                    });
                });
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(PrimaryHex);
                col.Item().PaddingBottom(8);
            });
        }

        private static void ComposeContent(
            IContainer container, Payment payment, Student? student, Invoice? invoice, User? donor, bool isDonation,
            IReadOnlyList<ReceiptConsolidatedLine>? consolidatedLines, string payerPhone)
        {
            var isConsolidated = consolidatedLines is { Count: > 0 };
            container.Column(col =>
            {
                // Paiement GLOBAL : tableau « élèves concernés » à la place du
                // bloc élève unique.
                if (isConsolidated)
                {
                    col.Item().Background(SurfaceVariant).Padding(10).Column(c =>
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Paiement groupé — ").SemiBold().FontColor(TextSecondary);
                            t.Span($"{consolidatedLines!.Count} enfant(s)").Bold();
                        });
                    });
                    col.Item().PaddingTop(8).Element(e => ComposeChildrenTable(e, consolidatedLines!));
                    col.Item().PaddingTop(10).Element(el => ComposeAmountsTable(el, payment));
                    col.Item().PaddingTop(10).Element(el => ComposeReferences(el, payment));
                    col.Item().PaddingTop(12).Text(t =>
                    {
                        t.Span("Document généré électroniquement, sans signature. ").FontSize(8).FontColor(TextSecondary);
                        if (payment.Status == PaymentStatus.Completed)
                            t.Span("Ce reçu atteste de la bonne réception du paiement.").FontSize(8).FontColor(TextSecondary);
                    });
                    return;
                }

                // Bloc élève / objet du paiement — ou bloc DONATEUR pour un don.
                col.Item().Background(SurfaceVariant).Padding(10).Column(c =>
                {
                    if (isDonation)
                    {
                        // Un don d'organisation est fait par l'ORGANISATION : son
                        // nom passe en tête, le représentant vient dessous. Un don
                        // par lien n'a pas de compte : le nom est celui déclaré.
                        var isOrganization = !string.IsNullOrWhiteSpace(payment.DonorOrganization)
                            || donor?.DonorType == DonorType.Organization;
                        var displayedDonor = FirstNonBlank(
                            payment.DonorOrganization, payment.DonorName, donor?.FullName) ?? "Donateur";

                        c.Item().Text(t =>
                        {
                            t.Span("Donateur : ").SemiBold().FontColor(TextSecondary);
                            t.Span(displayedDonor).Bold();
                        });
                        if (!string.IsNullOrWhiteSpace(payment.DonorOrganization)
                            && !string.IsNullOrWhiteSpace(payment.DonorName))
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Représentée par : ").SemiBold().FontColor(TextSecondary);
                                t.Span(payment.DonorName!);
                            });
                        }
                        c.Item().Text(t =>
                        {
                            t.Span("Type : ").SemiBold().FontColor(TextSecondary);
                            t.Span(isOrganization ? "Organisation" : "Particulier");
                        });
                        if (!string.IsNullOrWhiteSpace(payerPhone))
                            c.Item().Text(t =>
                            {
                                t.Span("Téléphone : ").SemiBold().FontColor(TextSecondary);
                                t.Span(payerPhone);
                            });
                        c.Item().Text(t =>
                        {
                            t.Span("Objet : ").SemiBold().FontColor(TextSecondary);
                            // Le donateur a donné POUR quelque chose : son reçu
                            // doit le nommer, pas dire « don à l'école ».
                            t.Span(string.IsNullOrWhiteSpace(payment.DonationCampaign?.Name)
                                ? "Don à l'école"
                                : payment.DonationCampaign!.Name);
                        });
                    }
                    else if (student != null)
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Élève : ").SemiBold().FontColor(TextSecondary);
                            t.Span($"{student.FirstName} {student.LastName}").Bold();
                        });
                        if (!string.IsNullOrWhiteSpace(student.StudentNumber))
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Matricule : ").SemiBold().FontColor(TextSecondary);
                                t.Span(student.StudentNumber!);
                            });
                        }
                        // Le numéro du payeur : la contrepartie d'un encaissement,
                        // comme le numéro du bénéficiaire l'est d'un virement.
                        if (!string.IsNullOrWhiteSpace(payerPhone))
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Téléphone du payeur : ").SemiBold().FontColor(TextSecondary);
                                t.Span(payerPhone);
                            });
                        }
                    }
                    // Objet du paiement (hors don — le bloc don ci-dessus est complet).
                    if (!isDonation)
                    {
                        if (invoice != null)
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Période facturée : ").SemiBold().FontColor(TextSecondary);
                                t.Span($"{invoice.PeriodStart:dd/MM/yyyy} → {invoice.PeriodEnd:dd/MM/yyyy}");
                            });
                        }
                        else
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Type : ").SemiBold().FontColor(TextSecondary);
                                // Topup wallet école = pas d'élève + pas de facture.
                                t.Span(student == null
                                    ? "Recharge du wallet école"
                                    : "Paiement libre");
                            });
                        }
                    }
                });

                // Tableau montants
                col.Item().PaddingTop(10).Element(e => ComposeAmountsTable(e, payment));

                // Références de réconciliation (la nôtre + celle du prestataire)
                col.Item().PaddingTop(10).Element(e => ComposeReferences(e, payment));

                // Mention finale
                col.Item().PaddingTop(12).Text(t =>
                {
                    t.Span("Document généré électroniquement, sans signature. ").FontSize(8).FontColor(TextSecondary);
                    if (payment.Status == PaymentStatus.Completed)
                    {
                        t.Span("Ce reçu atteste de la bonne réception du paiement.").FontSize(8).FontColor(TextSecondary);
                    }
                });
            });
        }

        private static void ComposeChildrenTable(
            IContainer container, IReadOnlyList<ReceiptConsolidatedLine> lines)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                table.Cell().Background(PrimaryHex).PaddingVertical(4).PaddingHorizontal(6)
                    .Text("Élève").FontColor(Colors.White).SemiBold().FontSize(9);
                table.Cell().Background(PrimaryHex).PaddingVertical(4).PaddingHorizontal(6)
                    .Text("Période").FontColor(Colors.White).SemiBold().FontSize(9);
                table.Cell().Background(PrimaryHex).PaddingVertical(4).PaddingHorizontal(6).AlignRight()
                    .Text("Montant").FontColor(Colors.White).SemiBold().FontSize(9);

                foreach (var l in lines)
                {
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6)
                        .Text(l.StudentName).FontSize(9);
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6)
                        .Text(l.PeriodLabel).FontSize(9).FontColor(TextSecondary);
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6).AlignRight()
                        .Text($"{l.AmountFcfa:N0} FCFA").FontSize(9);
                }
            });
        }

        private static void ComposeAmountsTable(IContainer container, Payment payment)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(1);
                });

                // Header : montant débité
                table.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6)
                    .Text("Montant débité").FontColor(Colors.White).SemiBold();
                table.Cell().Background(PrimaryHex).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                    .Text($"{payment.AmountFcfa:N0} FCFA").FontColor(Colors.White).SemiBold();

                // Détail frais (FeesPayer=Parent) : montant EXACT réellement majoré
                // = débité − cible (indépendant du % courant, qui peut changer à
                // tout moment). Fallback /1.08 pour d'anciens Payments sans cible.
                if (payment.FeesPayer == FeesPayer.Parent)
                {
                    var approxFees = payment.TargetAmountFcfa > 0
                        ? payment.AmountFcfa - payment.TargetAmountFcfa
                        : payment.AmountFcfa - (long)Math.Round(payment.AmountFcfa / 1.08m);
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6)
                        .Text("  dont frais transaction").FontSize(9).FontColor(TextSecondary);
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6).AlignRight()
                        .Text($"~{approxFees:N0} FCFA").FontSize(9).FontColor(TextSecondary);
                }

                if (payment.NetCreditedFcfa > 0)
                {
                    // Montant réellement crédité au wallet école. En FeesPayer=Parent
                    // c'est le montant CIBLE fixé par l'école (§82), PAS le net SenePay :
                    // le wallet reçoit 500 (cible), pas 529 (net). Afficher le net ici
                    // contredirait "montant débité 540 − frais 40 = 500" et le solde
                    // wallet réel. Fallback net pour les anciens Payments sans cible.
                    var creditedToSchool =
                        payment.FeesPayer == FeesPayer.Parent && payment.TargetAmountFcfa > 0
                            ? payment.TargetAmountFcfa
                            : payment.NetCreditedFcfa;
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6)
                        .Text("Net crédité à l'école").FontSize(9).FontColor(TextSecondary);
                    table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(6).AlignRight()
                        .Text($"{creditedToSchool:N0} FCFA").FontSize(9).FontColor(TextSecondary);
                }

                table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6)
                    .Text("Moyen de paiement");
                table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6).AlignRight()
                    .Text(FrenchOperator(payment.Operator));

                // La référence SenePay n'est PLUS ici : dans la colonne de droite
                // d'un tableau à deux colonnes sur une page A5, un jeton d'une
                // quarantaine de caractères sans espace n'a pas la place de se
                // replier proprement. Elle vit désormais dans le bloc « Références »
                // en pleine largeur, avec la référence Idara (cf. ComposeReferences).

                table.Cell().PaddingVertical(6).PaddingHorizontal(6)
                    .Text("Statut").Bold();
                var statusCell = table.Cell().PaddingVertical(6).PaddingHorizontal(6).AlignRight()
                    .Text(FrenchStatus(payment.Status)).Bold();
                if (payment.Status == PaymentStatus.Completed)
                    statusCell.FontColor(PrimaryHex);
            });
        }

        /// <summary>
        /// Bloc « Références » : celle d'Idara et celle du prestataire, en pleine
        /// largeur. Même présentation que sur un reçu de virement — deux documents
        /// de la même plateforme ne doivent pas ranger la même information à deux
        /// endroits différents.
        /// </summary>
        private static void ComposeReferences(IContainer container, Payment payment) =>
            PdfBlocks.References(
                container,
                IdaraReference.Payment(payment.Id),
                payment.SenePayTransactionId,
                TextSecondary,
                Border);

        private static void ComposeFooter(IContainer container, bool isDonation)
        {
            container.Column(col =>
            {
                // ⚠️ Mention indispensable sur un reçu de don : sans elle, un
                // donateur peut croire tenir une pièce fiscale déductible — et
                // c'est l'éditeur du document qu'on viendra voir.
                if (isDonation)
                {
                    col.Item().AlignCenter().Text(
                        "Ce reçu atteste d'un don volontaire. Il ne constitue pas une pièce fiscale "
                        + "et n'ouvre droit à aucune déduction.")
                        .FontColor(TextSecondary).FontSize(7);
                    col.Item().PaddingTop(2);
                }
                col.Item().AlignCenter().Text(t =>
                {
                    t.Span("Édité par ").FontColor(TextSecondary).FontSize(7);
                    t.Span("Idara").Bold().FontColor(PrimaryHex).FontSize(7);
                    t.Span($" — {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC").FontColor(TextSecondary).FontSize(7);
                });
            });
        }

        /// <summary>Première valeur non vide, ou null.</summary>
        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static string FrenchStatus(PaymentStatus s) => s switch
        {
            PaymentStatus.Completed => "Payé",
            PaymentStatus.Pending => "En attente",
            PaymentStatus.Failed => "Échec",
            PaymentStatus.Cancelled => "Annulé",
            PaymentStatus.Expired => "Expiré",
            _ => s.ToString()
        };

        private static string FrenchOperator(PaymentOperator op) => op switch
        {
            PaymentOperator.Wave => "Wave",
            PaymentOperator.Orange => "Orange Money",
            _ => op.ToString()
        };
    }
}
