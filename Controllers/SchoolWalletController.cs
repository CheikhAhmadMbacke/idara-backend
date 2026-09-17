using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Export;
using Idara.API.DTOs.Payment;
using Idara.API.DTOs.Wave;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Retraits du wallet école vers un compte Mobile Money (SenePay Payout).
    /// Saisie manuelle des coordonnées à chaque retrait (pas de comptes
    /// pré-enregistrés, spec §4.2). Step-up de sécurité = mot de passe SchoolAdmin
    /// vérifié côté serveur (remplace l'OTP retrait, jugé trop compliqué — cf.
    /// chantier auth retrait). Le mot de passe est saisi au verrou de l'écran
    /// paiement puis réutilisé ici.
    ///
    /// Modèle de frais (spec §4.4) : le wallet est DÉJÀ net de payout. L'école
    /// retire X (= ce qu'elle voit), le bénéficiaire reçoit X. On envoie à
    /// SenePay X / (1 − 1,77 %) pour absorber les frais opérateur.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/school/wallet")]
    public class SchoolWalletController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWaveClient _wave;
        private readonly IPaymentAvailabilityService _availability;
        private readonly IPayoutSettlementService _settlement;
        private readonly IMemoryCache _cache;
        private readonly IExportPdfService _exportPdf;
        private readonly ILogger<SchoolWalletController> _logger;

        // Montant minimum de retrait et frais payout (%) ne sont plus codés en
        // dur : lus depuis PlatformSettings (éditable SuperAdmin).

        public SchoolWalletController(
            AppDbContext context,
            IWaveClient wave,
            IPaymentAvailabilityService availability,
            IPayoutSettlementService settlement,
            IMemoryCache cache,
            IExportPdfService exportPdf,
            ILogger<SchoolWalletController> logger)
        {
            _context = context;
            _wave = wave;
            _availability = availability;
            _settlement = settlement;
            _cache = cache;
            _exportPdf = exportPdf;
            _logger = logger;
        }

        /// <summary>
        /// Exécute le retrait. Vérifie le mot de passe (step-up), réserve le montant
        /// (Available → Pending), appelle SenePay Payout, puis attend le webhook
        /// final (3.3) pour le débit définitif ou la restitution.
        /// </summary>
        [HttpPost("withdraw")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<WithdrawalDto>>> Withdraw(
            [FromBody] WithdrawRequestDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Unauthorized();

            // 🔴 Guichet de décaissement fermé ? On le demande AVANT de réserver
            // le moindre franc : une réservation posée puis restituée se voit
            // dans l'historique du solde et inquiète pour rien.
            var blocked = await _availability.PayoutBlockedReasonAsync(schoolId, ct);
            if (blocked is not null)
                return StatusCode(503, ApiResponse<WithdrawalDto>.Fail(blocked));

            // --- Résolution du bénéficiaire (carnet OU saisie ponctuelle) ---
            // La validation conditionnelle (champs manuels requis si pas de
            // BeneficiaryId, égalité des numéros, format) est faite par
            // WithdrawRequestDto.Validate (IValidatableObject).
            string recipientName;
            string recipientPhone;
            PaymentOperator operatorEnum;
            int? beneficiaryId = null;

            if (dto.BeneficiaryId != null)
            {
                var beneficiary = await _context.TransferBeneficiaries.FirstOrDefaultAsync(
                    b => b.Id == dto.BeneficiaryId.Value
                         && b.SchoolId == schoolId.Value
                         && !b.IsArchived, ct);
                if (beneficiary == null)
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        "Bénéficiaire introuvable ou archivé."));

                recipientName = beneficiary.Name;
                recipientPhone = beneficiary.Phone;
                operatorEnum = beneficiary.Operator;
                beneficiaryId = beneficiary.Id;
            }
            else
            {
                // Saisie manuelle — champs garantis non-null/valides par le DTO.
                operatorEnum = ParseOperator(dto.Operator!);
                recipientName = dto.RecipientName!.Trim();
                recipientPhone = dto.RecipientPhone!;
            }

            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (admin == null) return Unauthorized();

            // --- Step-up de sécurité : mot de passe SchoolAdmin (remplace l'OTP) ---
            // Rate-limité par utilisateur (15 min) pour empêcher le brute-force du
            // mot de passe via une session valide. Le mot de passe est réutilisable
            // (pas single-use comme l'OTP) → vérifié AVANT la transaction wallet,
            // pour échouer vite sans réserver de fonds. Clé PARTAGÉE avec
            // /auth/verify-password (`pwdreauth:{userId}`) : un attaquant n'a pas
            // deux budgets séparés contre le même secret (gate + retrait).
            var rlKey = $"pwdreauth:{userId.Value}";
            if (_cache.TryGetValue(rlKey, out int pwdAttempts) && pwdAttempts >= 5)
                return StatusCode(429, ApiResponse<WithdrawalDto>.Fail(
                    "Trop de tentatives. Réessayez dans quelques minutes."));
            if (!BCrypt.Net.BCrypt.Verify(dto.Password, admin.PasswordHash))
            {
                _cache.Set(rlKey, pwdAttempts + 1, TimeSpan.FromMinutes(15));
                return BadRequest(ApiResponse<WithdrawalDto>.Fail("Mot de passe incorrect."));
            }
            _cache.Remove(rlKey);

            // Réglages globaux (min retrait + frais payout %), éditables SuperAdmin.
            var platform = await _context.GetPlatformSettingsAsync(ct);

            // Montant minimum : check statique avant toute transaction.
            if (dto.Amount < platform.MinWithdrawalFcfa)
                return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                    $"Le montant minimum est de {platform.MinWithdrawalFcfa} FCFA."));

            // Montant EXACT envoyé à SenePay — plus de majoration côté Idara.
            // Depuis le modèle de frais SenePay 2026, on force fee_mode="on_top"
            // (cf. SenePayPayoutRequest.FeeMode) : le bénéficiaire reçoit
            // précisément dto.Amount, les frais opérateur (~1,77%) sont prélevés
            // EN PLUS sur la réserve marchand. L'ancienne majoration `dto.Amount /
            // (1 - PayoutFeeRate)` faisait SUR-verser le bénéficiaire (bug réel :
            // retrait de 500 → 510 reçu), car le nouveau modèle SenePay verse le
            // montant saisi tel quel.
            //
            // 🔑 `PayoutFeePercent` ne sert donc plus ICI, mais il n'est pas
            // décoratif pour autant : c'est lui qui, dans
            // PlatformSettings.ParentFeeMultiplier, fait provisionner ce frais
            // de sortie DÈS l'encaissement. Sans quoi la plateforme l'avancerait
            // — ce qu'elle a fait quatre mois durant (55 429 F sur 4 mois).
            // ⚠️ En mode FeesPayer=School, elle l'avance toujours : le wallet
            // n'a été crédité que du net d'entrée (§145).
            var sepayAmount = dto.Amount;

            var withdrawal = new Withdrawal
            {
                SchoolId = schoolId.Value,
                AmountFcfa = dto.Amount,
                SepayAmountFcfa = sepayAmount,
                Operator = operatorEnum,
                Category = dto.Category,
                CategoryLabel = dto.Category == TransferCategory.Other
                    ? dto.CategoryLabel?.Trim()
                    : null,
                Motif = string.IsNullOrWhiteSpace(dto.Motif) ? null : dto.Motif!.Trim(),
                BeneficiaryId = beneficiaryId,
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
                Source = dto.Source,
                Status = WithdrawalStatus.Initiated,
                InitiatedById = userId.Value,
                CreatedAt = DateTime.UtcNow
            };

            // --- Verrou wallet + check solde + réservation, ATOMIQUEMENT ---
            // SELECT ... FOR UPDATE sérialise tout mouvement concurrent sur ce
            // wallet (deux retraits simultanés, retrait vs crédit payin, vs
            // webhook) : ni sur-débit ni lost update possibles. Le mot de passe
            // (step-up) est déjà vérifié en amont. On COMMIT avant l'appel SenePay
            // (anti-race webhook, même logique que le payin §1.4).
            await using (var tx = await _context.Database.BeginTransactionAsync(ct))
            {
                var wallet = await _context.LockWalletAsync(schoolId.Value, ct);
                if (wallet == null)
                {
                    await tx.RollbackAsync(ct);
                    return StatusCode(500, ApiResponse<WithdrawalDto>.Fail("Wallet introuvable."));
                }

                // Check de solde PAR POCHE (le daara a choisi Total / Paiements /
                // Dons). Sous le verrou → l'état est figé.
                if (!wallet.HasEnoughForSource(dto.Amount, dto.Source))
                {
                    await tx.RollbackAsync(ct);
                    var (label, avail) = dto.Source switch
                    {
                        WithdrawalSource.Fee => ("solde paiement", wallet.FeeBalance()),
                        WithdrawalSource.Donation => ("solde don", wallet.DonationBalanceFcfa),
                        _ => ("solde disponible", wallet.AvailableBalance)
                    };
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        $"Solde insuffisant. {char.ToUpper(label[0]) + label[1..]} : {avail} FCFA."));
                }

                // --- Idempotence anti double-dépense ---
                // L'OTP à usage unique (consommé sous CE verrou) protégeait contre
                // le rejeu : double-tap, deux onglets, OU retry réseau après un
                // succès dont la réponse s'est perdue. Le mot de passe qui le
                // remplace est RÉUTILISABLE → on bloque tout retrait identique
                // (même école, même montant, même bénéficiaire) initié dans les
                // 60 dernières s et non échoué. Sous le verrou FOR UPDATE, deux
                // requêtes concurrentes sont sérialisées : la 2e voit la 1re
                // committée (READ COMMITTED) → rejetée. Les retraits Failed
                // (restitués) ne bloquent pas un nouvel essai légitime.
                var dupSince = DateTime.UtcNow.AddSeconds(-60);
                var isDuplicate = await _context.Withdrawals.AnyAsync(w =>
                    w.SchoolId == schoolId.Value
                    && w.AmountFcfa == dto.Amount
                    && w.RecipientPhone == recipientPhone
                    && w.Status != WithdrawalStatus.Failed
                    && w.CreatedAt >= dupSince, ct);
                if (isDuplicate)
                {
                    await tx.RollbackAsync(ct);
                    return Conflict(ApiResponse<WithdrawalDto>.Fail(
                        "Un retrait identique vient d'être initié. Vérifiez l'historique avant de réessayer."));
                }

                // Part prélevée sur la poche « Don » (figée sur le retrait pour une
                // restitution exacte en cas d'échec, cf. PayoutSettlementService).
                var donationDraw = wallet.DonationDrawFor(dto.Amount, dto.Source);
                withdrawal.DonationAmountFcfa = donationDraw;

                _context.Withdrawals.Add(withdrawal);
                withdrawal.Provider = PaymentProviders.Wave;
                await _context.SaveChangesAsync(ct); // assigne withdrawal.Id

                wallet.AvailableBalance -= dto.Amount;
                wallet.PendingBalance += dto.Amount;
                wallet.DonationBalanceFcfa -= donationDraw; // la poche don suit la réservation
                wallet.UpdatedAt = DateTime.UtcNow;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    SchoolId = schoolId.Value,
                    Type = WalletTransactionType.Reservation,
                    Source = WalletSource.Withdrawal,
                    AmountFcfa = -dto.Amount, // réservation = signé négatif
                    BalanceAfter = wallet.AvailableBalance,
                    RelatedEntity = WalletRelatedEntity.Withdrawal,
                    RelatedId = withdrawal.Id,
                    Note = $"Réservation retrait #{withdrawal.Id}",
                    OccurredAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            // --- Décaissement Wave (hors transaction) ---
            WavePayout resp;
            try
            {
                resp = await _wave.CreatePayoutAsync(new WaveCreatePayoutRequest
                {
                    Currency = "XOF",
                    Mobile = "+221" + recipientPhone,
                    // 🔑 `receive_amount` est ce que le bénéficiaire TOUCHE. Les
                    // frais Wave s'ajoutent au débit de notre compte : le daara
                    // reçoit exactement ce qu'il a demandé, ni plus ni moins.
                    ReceiveAmount = WaveClient.FormatAmount(sepayAmount),
                    Name = recipientName,
                    ClientReference = withdrawal.Id.ToString(),
                    // Imprimé sur le reçu du bénéficiaire, 40 caractères au plus.
                    PaymentReason = $"Idara retrait ecole {schoolId.Value}"
                }, PayoutIdempotency.ForWithdrawal(withdrawal.Id), ct);
            }
            catch (WaveApiException ex)
            {
                // DURCISSEMENT ANTI DOUBLE DÉPENSE (§78), et il compte DOUBLE ici :
                // Wave n'émet aucun webhook de décaissement. Un timeout ne dit pas
                // que l'argent n'est pas parti — seule une relecture le dira.
                if (ex.IsDefinitiveRejection)
                {
                    // Refus AVANT exécution (numéro invalide, solde marchand
                    // insuffisant, bénéficiaire bloqué…) : aucun franc n'est
                    // sorti, on restitue la réservation.
                    var reason = ex.Code ?? ex.Message;
                    await _settlement.SettleFailedAsync(
                        withdrawal.Id, reason, null, null, "sync-init", ct);
                    _logger.LogWarning(ex,
                        "[withdraw] Wave {Status}/{Code} (rejet pré-exécution) Withdrawal {Id} — réservation restituée",
                        ex.StatusCode, ex.Code, withdrawal.Id);
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                        IsTemporaryPayoutOutage(reason)
                            ? PayoutUnavailableMsg
                            : "Le retrait a été refusé (coordonnées ou opérateur invalide). Votre solde a été restitué."));
                }

                // 5xx / 429 / timeout / réseau = INDÉTERMINÉ → fonds maintenus
                // réservés, et le travail de vérification interrogera
                // GET /v1/payout/{id} — ou retrouvera l'opération par notre
                // référence si l'identifiant ne nous est jamais parvenu.
                await _settlement.MarkUnderVerificationAsync(
                    withdrawal.Id, null, ex.Message, "sync-init", ct);
                await _context.Entry(withdrawal).ReloadAsync(ct);
                _logger.LogWarning(ex,
                    "[withdraw] Wave indéterminé (status={Status}) Withdrawal {Id} — statut={WStatus}",
                    ex.StatusCode, withdrawal.Id, withdrawal.Status);

                if (withdrawal.Status == WithdrawalStatus.Failed)
                    return BadRequest(ApiResponse<WithdrawalDto>.Fail(PayoutUnavailableMsg));

                return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal),
                    "Retrait en cours de vérification. Vous serez notifié dès confirmation."));
            }

            var statusLower = resp.Status?.ToLowerInvariant();

            // Rejet TERMINAL explicite (failed/cancelled) : aucun fonds sorti →
            // restitution + Failed.
            if (statusLower is "failed")
            {
                var err = resp.PayoutError?.Code ?? resp.PayoutError?.Message ?? statusLower;
                await _settlement.SettleFailedAsync(
                    withdrawal.Id, err, resp.Id, null, "sync-init", ct);
                _logger.LogWarning(
                    "[withdraw] Wave a rejeté le décaissement Withdrawal {Id} ({Err}) — réservation restituée",
                    withdrawal.Id, err);
                return BadRequest(ApiResponse<WithdrawalDto>.Fail(
                    IsTemporaryPayoutOutage(err)
                        ? PayoutUnavailableMsg
                        : "Le retrait a été refusé par l'opérateur. Votre solde a été restitué."));
            }

            // Succès SYNCHRONE (défensif — depuis le durcissement SenePay, le POST
            // ne renvoie plus `completed` synchrone en prod, mais on le gère par
            // sécurité si SenePay n'envoyait pas de webhook séparé).
            if (statusLower == "succeeded")
            {
                var fees = WaveClient.ParseAmount(resp.Fee);
                var net = WaveClient.ParseAmount(resp.ReceiveAmount);
                await _settlement.SettleCompletedAsync(
                    withdrawal.Id, resp.Id, fees, net, null, "sync-init", ct);
                await _context.Entry(withdrawal).ReloadAsync(ct);
                _logger.LogInformation(
                    "[withdraw] Withdrawal {Id} complété SYNCHRONE (School {SchoolId}, {Amount} FCFA)",
                    withdrawal.Id, schoolId.Value, dto.Amount);
                return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal), "Retrait effectué."));
            }

            // Tout le reste — submitted / processing / pending / pending_approval /
            // pending_verification / inconnu / success=false-sans-statut-terminal :
            // INDÉTERMINÉ. On garde les fonds réservés et on poll. C'est désormais
            // le chemin nominal (submitted = opérateur a accepté, confirmation à venir).
            await _settlement.MarkUnderVerificationAsync(
                withdrawal.Id, resp.Id, $"status={resp.Status}", "sync-init", ct);
            await _context.Entry(withdrawal).ReloadAsync(ct);

            _logger.LogInformation(
                "[withdraw] Withdrawal {Id} en vérification (School {SchoolId}, {Amount} FCFA, envoyé={Sent}, payout={PayoutId}, status={Status})",
                withdrawal.Id, schoolId.Value, dto.Amount, sepayAmount, resp.Id, resp.Status);

            return Ok(ApiResponse<WithdrawalDto>.Ok(MapToDto(withdrawal),
                "Retrait en cours de vérification. Vous serez notifié dès confirmation."));
        }

        /// <summary>
        /// Historique read-only des retraits de l'école (récents d'abord).
        /// Numéro bénéficiaire masqué. Lisible SchoolAdmin + SchoolStaff.
        /// </summary>
        [HttpGet("withdrawals")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<ActionResult<IEnumerable<WithdrawalDto>>> GetWithdrawals(
            [FromQuery] int take,
            [FromQuery] string? q,
            [FromQuery] TransferCategory? category,
            [FromQuery] WithdrawalStatus? status,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // Une recherche porte sur TOUT l'historique : on élargit la fenêtre,
            // sinon elle ne balaierait que les 50 derniers transferts.
            var limit = take is > 0 and <= 200 ? take
                : string.IsNullOrWhiteSpace(q) ? 50 : 300;

            var items = await FilterWithdrawals(
                    _context.Withdrawals.Where(w => w.SchoolId == schoolId.Value),
                    q, category, status)
                .OrderByDescending(w => w.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            return Ok(items.Select(MapToDto));
        }

        /// <summary>
        /// `GET /api/school/wallet/withdrawals/pdf?from&to` — l'historique des
        /// retraits & virements en PDF (tableau) à archiver ou partager. Mêmes
        /// lignes que l'écran Transferts, sur la période choisie.
        /// </summary>
        [HttpGet("withdrawals/pdf")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> GetWithdrawalsPdf(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] string? q,
            [FromQuery] TransferCategory? category,
            [FromQuery] WithdrawalStatus? status,
            CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            // MÊME filtrage que la liste : l'export ne peut pas montrer autre
            // chose que ce que l'écran affiche (§116).
            var query = FilterWithdrawals(
                _context.Withdrawals.Where(w => w.SchoolId == schoolId.Value),
                q, category, status);

            // Bornes en jour civil sur la date de création (celle qui fait foi
            // dans l'écran Transferts).
            if (from.HasValue) query = query.Where(w => w.CreatedAt >= from.Value.ToUtcDay());
            if (to.HasValue) query = query.Where(w => w.CreatedAt < to.Value.ToUtcDay().AddDays(1));

            var items = await query
                .OrderByDescending(w => w.CreatedAt)
                .Take(FinanceLabels.MaxExportRows)
                .ToListAsync(ct);

            var rows = items.Select(w => new TransactionPdfRow
            {
                Date = w.CompletedAt ?? w.CreatedAt,
                Title = FinanceLabels.TransferCategory(w.Category, w.CategoryLabel),
                // Nom et numéro dans DEUX colonnes distinctes : concaténés, ils
                // empêchaient de trier ou de chercher sur l'un ou l'autre (§120).
                Subtitle = string.IsNullOrWhiteSpace(w.RecipientName) ? null : w.RecipientName,
                Phone = string.IsNullOrWhiteSpace(w.RecipientPhone) ? null : w.RecipientPhone,
                Note = w.Motif,
                Method = FinanceLabels.Operator(w.Operator),
                Reference = w.ProviderDisbursementId,
                Status = FinanceLabels.WithdrawalStatus(w.Status),
                // Un retrait est une SORTIE du wallet → montant négatif.
                AmountFcfa = -w.AmountFcfa
            }).ToList();

            // Seuls les virements réellement effectués comptent dans le total sorti :
            // un retrait échoué a été restitué au solde.
            var completed = items.Where(w => w.Status == WithdrawalStatus.Completed).Sum(w => w.AmountFcfa);
            var pending = items
                .Where(w => w.Status is WithdrawalStatus.Initiated or WithdrawalStatus.UnderVerification)
                .Sum(w => w.AmountFcfa);
            var summary = new List<(string, string, bool)>
            {
                ("Total verse", $"{completed:N0}", true),
                ("En cours", $"{pending:N0}", false)
            };

            var schoolName = await _context.Schools.Where(s => s.Id == schoolId.Value)
                .Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "Daara";

            var bytes = _exportPdf.BuildTransactionsPdf(
                schoolName,
                FinanceLabels.ExportTitle("Historique des retraits et virements", rows.Count),
                from, to, rows, summary,
                // Toujours de l'argent sortant : la contrepartie est le beneficiaire.
                counterpartyHeader: "Bénéficiaire");

            return File(bytes, "application/pdf", "historique-transferts-idara.pdf");
        }

        /// <summary>
        /// `POST /api/school/wallet/withdrawals/{id}/hide` — masque un retrait de
        /// l'affichage école (cosmétique, comme les paiements/transactions ; ne
        /// touche NI au solde NI à la compta). Non réversible côté UI.
        /// </summary>
        [HttpPost("withdrawals/{id:int}/hide")]
        [Authorize(Roles = UserRoles.SchoolAdmin)]
        public async Task<ActionResult<ApiResponse<bool>>> HideWithdrawal(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var n = await _context.Withdrawals
                .Where(w => w.Id == id && w.SchoolId == schoolId.Value && !w.IsPlatform)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.IsHidden, true), ct);
            if (n == 0) return NotFound(ApiResponse<bool>.Fail("Retrait introuvable."));

            return Ok(ApiResponse<bool>.Ok(true, "Retrait masqué."));
        }

        /// <summary>
        /// `GET /api/school/wallet/withdrawals/{id}/receipt` — reçu PDF d'un retrait
        /// / virement COMPLÉTÉ, côté ÉCOLE (SchoolAdmin/Staff), scopé à son école.
        /// Sert au daara à partager la preuve d'un salaire/charge (WhatsApp).
        /// Réutilise le rendu du reçu de virement (même document que côté bénéficiaire).
        /// </summary>
        [HttpGet("withdrawals/{id:int}/receipt")]
        [Authorize(Roles = $"{UserRoles.SchoolAdmin},{UserRoles.SchoolStaff}")]
        public async Task<IActionResult> DownloadWithdrawalReceipt(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Unauthorized();

            var w = await _context.Withdrawals
                .Include(x => x.School)
                .FirstOrDefaultAsync(x => x.Id == id
                                          && x.SchoolId == schoolId.Value
                                          && !x.IsPlatform
                                          && x.Status == WithdrawalStatus.Completed, ct);
            if (w == null)
                return NotFound(ApiResponse<bool>.Fail("Reçu indisponible (retrait non complété)."));

            var bytes = _exportPdf.BuildTransferReceiptPdf(TransferReceiptFactory.From(w));

            return File(bytes, "application/pdf", $"recu-retrait-idara-{w.Id:D6}.pdf");
        }

        // ====================================================================
        // ===== Helpers =====
        // ====================================================================

        // Message utilisateur pour un échec TEMPORAIRE côté prestataire (float
        // opérateur/AfribaPay insuffisant, 502 gateway SenePay) — distinct d'une
        // vraie erreur de coordonnées. Le solde est TOUJOURS restitué dans ces cas.
        private const string PayoutUnavailableMsg =
            "Le retrait n'a pas pu être effectué : le service de paiement est momentanément indisponible. Votre solde a été restitué, réessayez plus tard.";

        /// <summary>
        /// Détecte une indisponibilité temporaire du décaissement (float provider
        /// insuffisant, erreur gateway 5xx) à partir du message d'erreur SenePay,
        /// pour afficher un message honnête plutôt que d'accuser les coordonnées.
        /// </summary>
        /// <summary>
        /// Délègue à <see cref="Common.Utilities.PayoutFailureClassifier"/>
        /// (2026-09-01). La logique vivait ici en propre ; depuis que l'alerte
        /// e-mail doit classer le MÊME motif pour décider de son urgence et de
        /// son conseil, deux copies auraient fini par se contredire — l'école
        /// lisant « coordonnées invalides » pendant que l'e-mail dirait
        /// « réserve à sec ».
        /// </summary>
        private static bool IsTemporaryPayoutOutage(string? reason) =>
            Common.Utilities.PayoutFailureClassifier.IsTemporaryOutage(reason);

        /// <summary>
        /// Filtres communs à la LISTE et à l'EXPORT des transferts : masqués
        /// exclus, recherche libre (bénéficiaire, numéro, motif, catégorie
        /// saisie, référence), nature et statut. Un seul endroit pour que
        /// l'écran et le PDF ne puissent jamais diverger (§116).
        /// </summary>
        private static IQueryable<Withdrawal> FilterWithdrawals(
            IQueryable<Withdrawal> query, string? search,
            TransferCategory? category, WithdrawalStatus? status)
        {
            query = query.Where(w => !w.IsHidden); // masqués par le daara

            if (category.HasValue) query = query.Where(w => w.Category == category.Value);
            if (status.HasValue) query = query.Where(w => w.Status == status.Value);

            if (TransactionSearch.Pattern(search) is string pattern)
            {
                // Le numéro est cherché aussi en « chiffres seuls » : « 77 123 45 67 »
                // et « 771234567 » doivent ramener le même virement.
                var digits = TransactionSearch.PhonePattern(search);
                query = query.Where(w =>
                    EF.Functions.ILike(AppDbContext.Unaccent(w.RecipientName), pattern)
                    || EF.Functions.ILike(AppDbContext.Unaccent(w.RecipientPhone), pattern)
                    || (digits != null && EF.Functions.ILike(AppDbContext.Unaccent(w.RecipientPhone), digits))
                    || (w.Motif != null && EF.Functions.ILike(AppDbContext.Unaccent(w.Motif), pattern))
                    || (w.CategoryLabel != null && EF.Functions.ILike(AppDbContext.Unaccent(w.CategoryLabel), pattern))
                    || (w.ProviderDisbursementId != null && EF.Functions.ILike(AppDbContext.Unaccent(w.ProviderDisbursementId), pattern)));
            }

            return query;
        }

        private static WithdrawalDto MapToDto(Withdrawal w) => new()
        {
            Id = w.Id,
            AmountFcfa = w.AmountFcfa,
            FeesFcfa = w.FeesFcfa,
            NetReceivedFcfa = w.NetReceivedFcfa,
            Operator = w.Operator,
            Category = w.Category,
            Source = w.Source,
            CategoryLabel = w.CategoryLabel,
            Motif = w.Motif,
            RecipientName = w.RecipientName,
            RecipientPhoneMasked = MaskPhone(w.RecipientPhone),
            RecipientPhone = w.RecipientPhone,
            Status = w.Status,
            Reference = IdaraReference.Withdrawal(w.Id),
            SenePayReference = w.ProviderDisbursementId,
            FailureReason = w.FailureReason,
            CreatedAt = w.CreatedAt,
            CompletedAt = w.CompletedAt,
            FailedAt = w.FailedAt
        };

        private static PaymentOperator ParseOperator(string op) => op.ToLowerInvariant() switch
        {
            "wave" => PaymentOperator.Wave,
            "orange" => PaymentOperator.Orange,
            _ => throw new ArgumentOutOfRangeException(nameof(op), $"Opérateur non supporté : {op}")
        };

        /// <summary>"771234567" → "77*****67".</summary>
        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
            return phone[..2] + new string('*', phone.Length - 4) + phone[^2..];
        }
    }
}
