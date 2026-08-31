using System.Security.Cryptography;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Espace de relecture des traductions, ouvert par LIEN.
    ///
    /// Le relecteur n'a pas de compte Idara : un arabophone qui accepte de
    /// relire n'est pas forcément utilisateur de la plateforme, et lui imposer
    /// une inscription ferait perdre la moitié des bonnes volontés. Le jeton de
    /// 128 bits contenu dans l'URL EST l'authentification — même principe que
    /// le lien de paiement (§161).
    ///
    /// Rien ne se perd : chaque frappe est enregistrée en base.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("tr")]
    public class TranslationsPublicController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public TranslationsPublicController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        /// <summary>La page de relecture elle-même.</summary>
        [HttpGet("{token}")]
        public IActionResult Page(string token)
        {
            var path = Path.Combine(_env.WebRootPath, "tr", "review.html");
            if (!System.IO.File.Exists(path)) return NotFound();
            // `no-store` : la page porte l'état du travail, elle ne doit jamais
            // être servie depuis un cache (§81).
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return PhysicalFile(path, "text/html; charset=utf-8");
        }

        /// <summary>
        /// Adresse MÉMORISABLE : <c>api.idara.sn/traduction/arabe</c> ou
        /// <c>/traduction/wolof</c>.
        ///
        /// Elle sert le même écran que le lien à jeton. La différence est
        /// qu'elle se partage dans un groupe WhatsApp ou se dicte au téléphone
        /// — un jeton de 32 caractères, non.
        ///
        /// L'adresse est ouverte, et c'est assumé : le pire qu'un inconnu
        /// puisse faire est de PROPOSER une traduction. Rien ne part en
        /// production sans un export relu par le SuperAdmin et un déploiement.
        /// Chaque contributeur donne son nom au premier accès et n'écrase donc
        /// jamais le travail d'un autre.
        /// </summary>
        [HttpGet("/traduction/{langue}")]
        public IActionResult LanguePage(string langue)
        {
            if (LangCodeOf(langue) == null) return NotFound();
            var path = Path.Combine(_env.WebRootPath, "tr", "review.html");
            if (!System.IO.File.Exists(path)) return NotFound();
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return PhysicalFile(path, "text/html; charset=utf-8");
        }

        /// <summary>Nom français d'une langue → code interne. Null si inconnue.</summary>
        private static string? LangCodeOf(string? langue) => (langue ?? "").ToLowerInvariant() switch
        {
            "arabe" or "ar" => "ar",
            "wolof" or "wo" => "wo",
            _ => null,
        };

        public class JoinDto
        {
            public string Name { get; set; } = string.Empty;
        }

        /// <summary>
        /// Un contributeur se présente et reçoit son espace de travail.
        ///
        /// Le MÊME nom retrouve le MÊME espace : quelqu'un qui change de
        /// téléphone, ou qui revient une semaine plus tard, retrouve ses
        /// propositions au lieu de tout refaire.
        /// </summary>
        [HttpPost("/traduction/{langue}/rejoindre")]
        public async Task<IActionResult> Rejoindre(string langue, [FromBody] JoinDto dto, CancellationToken ct)
        {
            var lang = LangCodeOf(langue);
            if (lang == null) return NotFound(new { error = "Langue inconnue." });

            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length is < 2 or > 60)
                return BadRequest(new { error = "Indiquez votre nom (entre 2 et 60 caractères)." });

            // Même nom, même langue = même espace. La comparaison ignore la
            // casse : « Modou » et « modou » sont la même personne.
            var existing = await _context.TranslationReviewers
                .FirstOrDefaultAsync(r => r.Lang == lang && r.Name.ToLower() == name.ToLower(), ct);

            if (existing != null)
            {
                if (!existing.IsActive)
                    return StatusCode(403, new { error = "Cet accès a été désactivé." });
                existing.LastSeenAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                return Ok(new { token = existing.Token, name = existing.Name });
            }

            // Garde-fou de volume : l'adresse est publique, on ne laisse pas
            // fabriquer des milliers d'espaces vides.
            var count = await _context.TranslationReviewers.CountAsync(r => r.Lang == lang, ct);
            if (count >= 200)
                return StatusCode(429, new { error = "Trop de contributeurs pour cette langue. Contactez Idara." });

            var rev = new TranslationReviewer
            {
                Name = name,
                Lang = lang,
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            };
            _context.TranslationReviewers.Add(rev);
            await _context.SaveChangesAsync(ct);
            return Ok(new { token = rev.Token, name = rev.Name });
        }

        private async Task<TranslationReviewer?> ReviewerAsync(string token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;
            return await _context.TranslationReviewers
                .FirstOrDefaultAsync(r => r.Token == token && r.IsActive, ct);
        }

        /// <summary>
        /// État de travail du relecteur : ses sections, leur avancement, et le
        /// contenu d'une section demandée.
        ///
        /// La page ne charge JAMAIS les 1660 clés d'un coup : sur un forfait
        /// sénégalais ce serait plusieurs secondes et un écran illisible. Elle
        /// charge la liste des sections, puis une section à la fois.
        /// </summary>
        [HttpGet("{token}/state")]
        public async Task<IActionResult> State(string token, [FromQuery] string? section, CancellationToken ct)
        {
            var rev = await ReviewerAsync(token, ct);
            if (rev == null) return NotFound(new { error = "Lien inconnu ou désactivé." });

            rev.LastSeenAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            var keys = _context.TranslationKeys.Where(k => !k.IsObsolete);

            // Avancement par section : « fait » = une proposition non vide de CE
            // relecteur, ou (pour l'arabe) une valeur déjà présente qu'il n'a pas
            // eu à corriger. On compte donc ce qui RESTE à regarder.
            var mine = _context.TranslationProposals
                .Where(p => p.ReviewerId == rev.Id && p.Lang == rev.Lang && p.Text != "");

            var sections = await keys
                .GroupBy(k => k.Section)
                .Select(g => new
                {
                    section = g.Key,
                    total = g.Count(),
                    proposed = g.Count(k => mine.Any(p => p.TranslationKeyId == k.Id)),
                })
                .ToListAsync(ct);

            // Nom FRANÇAIS de chaque partie : « cred » ou « kyc » ne veut rien
            // dire pour un relecteur bénévole. Le tri se fait sur le libellé
            // affiché, pour que la liste soit dans l'ordre qu'il LIT.
            var sectionsLabeled = sections
                .Select(x => new
                {
                    x.section,
                    label = TranslationSections.Label(x.section),
                    x.total,
                    x.proposed,
                })
                .OrderBy(x => x.label, StringComparer.CurrentCulture)
                .ToList();

            object? rows = null;
            if (!string.IsNullOrWhiteSpace(section))
            {
                var raw = await keys.Where(k => k.Section == section)
                    .OrderBy(k => k.Key)
                    .Select(k => new
                    {
                        k.Id,
                        k.Key,
                        k.Fr,
                        k.Ar,
                        k.Wo,
                        proposal = _context.TranslationProposals
                            .Where(p => p.TranslationKeyId == k.Id && p.ReviewerId == rev.Id && p.Lang == rev.Lang)
                            .Select(p => new { p.Text, p.Note, p.BasedOn })
                            .FirstOrDefault(),
                    })
                    .ToListAsync(ct);

                rows = raw.Select(k =>
                {
                    var current = rev.Lang == "ar" ? k.Ar : k.Wo;
                    return new
                    {
                        id = k.Id,
                        key = k.Key,
                        fr = k.Fr,
                        current,
                        proposal = k.proposal?.Text ?? "",
                        note = k.proposal?.Note ?? "",
                        // Le texte de référence a changé depuis la proposition :
                        // on le signale sans rien rejeter.
                        stale = k.proposal != null && TranslationTools.IsStale(k.proposal.BasedOn, k.Fr),
                    };
                }).ToList();
            }

            return Ok(new
            {
                reviewer = rev.Name,
                lang = rev.Lang,
                langLabel = rev.Lang == "ar" ? "arabe" : "wolof",
                sections = sectionsLabeled,
                sectionLabel = string.IsNullOrWhiteSpace(section)
                    ? null : TranslationSections.Label(section!),
                rows,
            });
        }

        public class ProposeDto
        {
            public int KeyId { get; set; }
            public string? Text { get; set; }
            public string? Note { get; set; }
        }

        /// <summary>
        /// Enregistre (ou met à jour) la proposition du relecteur pour une clé.
        /// Appelé à chaque pause de frappe : c'est ce qui garantit qu'aucune
        /// correction n'est perdue si l'onglet se ferme ou si le réseau tombe.
        /// </summary>
        [HttpPut("{token}/propose")]
        public async Task<IActionResult> Propose(string token, [FromBody] ProposeDto dto, CancellationToken ct)
        {
            var rev = await ReviewerAsync(token, ct);
            if (rev == null) return NotFound(new { error = "Lien inconnu ou désactivé." });

            var key = await _context.TranslationKeys.FirstOrDefaultAsync(k => k.Id == dto.KeyId, ct);
            if (key == null) return NotFound(new { error = "Clé inconnue." });

            var text = (dto.Text ?? string.Empty).Trim();
            if (text.Length > 4000) return BadRequest(new { error = "Texte trop long." });
            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (note is { Length: > 1000 }) note = note[..1000];

            var now = DateTime.UtcNow;
            var existing = await _context.TranslationProposals.FirstOrDefaultAsync(
                p => p.TranslationKeyId == key.Id && p.ReviewerId == rev.Id && p.Lang == rev.Lang, ct);

            if (existing == null)
            {
                existing = new TranslationProposal
                {
                    TranslationKeyId = key.Id,
                    ReviewerId = rev.Id,
                    Lang = rev.Lang,
                    CreatedAt = now,
                };
                _context.TranslationProposals.Add(existing);
            }

            existing.Text = text;
            existing.Note = note;
            existing.BasedOn = key.Fr;
            existing.UpdatedAt = now;
            rev.LastSeenAt = now;

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Deux onglets ouverts sur la même clé : la seconde écriture
                // heurte l'unicité. On relit et on applique — perdre une frappe
                // serait pire que d'écraser la précédente du MÊME relecteur.
                _context.Entry(existing).State = EntityState.Detached;
                var again = await _context.TranslationProposals.FirstAsync(
                    p => p.TranslationKeyId == key.Id && p.ReviewerId == rev.Id && p.Lang == rev.Lang, ct);
                again.Text = text;
                again.Note = note;
                again.BasedOn = key.Fr;
                again.UpdatedAt = now;
                await _context.SaveChangesAsync(ct);
            }

            return Ok(new { saved = true });
        }
    }

    /// <summary>Pilotage de l'outil de relecture : import, liens, export.</summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    [Route("api/admin/translations")]
    public class TranslationsAdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<TranslationsAdminController> _logger;

        public TranslationsAdminController(AppDbContext context, ILogger<TranslationsAdminController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public class ImportDto
        {
            /// <summary>Contenu de `fr.json`.</summary>
            public string Fr { get; set; } = string.Empty;
            /// <summary>Contenu de `ar.json`.</summary>
            public string? Ar { get; set; }
            /// <summary>Contenu de `wo.json`, quand il existera.</summary>
            public string? Wo { get; set; }
        }

        /// <summary>
        /// Charge (ou recharge) les clés depuis les JSON du frontend.
        ///
        /// Idempotent, et surtout NON DESTRUCTIF : les clés disparues sont
        /// marquées obsolètes mais conservées avec leurs propositions — une clé
        /// peut s'absenter le temps d'une refonte et revenir, et la supprimer
        /// emporterait le travail déjà fait.
        /// </summary>
        [HttpPost("import")]
        public async Task<IActionResult> Import([FromBody] ImportDto dto, CancellationToken ct)
        {
            Dictionary<string, string> fr, ar, wo;
            try
            {
                fr = TranslationTools.Flatten(dto.Fr);
                ar = string.IsNullOrWhiteSpace(dto.Ar) ? new() : TranslationTools.Flatten(dto.Ar!);
                wo = string.IsNullOrWhiteSpace(dto.Wo) ? new() : TranslationTools.Flatten(dto.Wo!);
            }
            catch (System.Text.Json.JsonException ex)
            {
                return BadRequest(ApiResponse<bool>.Fail("JSON illisible : " + ex.Message));
            }

            if (fr.Count == 0)
                return BadRequest(ApiResponse<bool>.Fail("Le fichier français est vide."));

            var now = DateTime.UtcNow;
            var existing = await _context.TranslationKeys.ToDictionaryAsync(k => k.Key, ct);
            int added = 0, updated = 0, obsolete = 0;

            foreach (var (key, value) in fr)
            {
                ar.TryGetValue(key, out var arV);
                wo.TryGetValue(key, out var woV);

                if (existing.TryGetValue(key, out var row))
                {
                    if (row.Fr != value || row.Ar != arV || row.Wo != woV || row.IsObsolete)
                    {
                        row.Fr = value;
                        row.Ar = arV;
                        row.Wo = woV;
                        row.IsObsolete = false;
                        row.UpdatedAt = now;
                        updated++;
                    }
                }
                else
                {
                    _context.TranslationKeys.Add(new TranslationKey
                    {
                        Key = key,
                        Fr = value,
                        Ar = arV,
                        Wo = woV,
                        Section = TranslationTools.SectionOf(key),
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    added++;
                }
            }

            foreach (var (key, row) in existing)
            {
                if (!fr.ContainsKey(key) && !row.IsObsolete)
                {
                    row.IsObsolete = true;
                    row.UpdatedAt = now;
                    obsolete++;
                }
            }

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[i18n] Import : {Added} ajoutées, {Updated} mises à jour, {Obsolete} obsolètes ({Total} clés au total)",
                added, updated, obsolete, fr.Count);

            return Ok(ApiResponse<object>.Ok(
                new { added, updated, obsolete, total = fr.Count },
                $"{added} clé(s) ajoutée(s), {updated} mise(s) à jour, {obsolete} devenue(s) obsolète(s)."));
        }

        public class ReviewerDto
        {
            public string Name { get; set; } = string.Empty;
            public string Lang { get; set; } = "ar";
        }

        /// <summary>Crée un relecteur et son lien.</summary>
        [HttpPost("reviewers")]
        public async Task<IActionResult> CreateReviewer([FromBody] ReviewerDto dto, CancellationToken ct)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length is < 2 or > 120)
                return BadRequest(ApiResponse<bool>.Fail("Nom du relecteur requis (2 à 120 caractères)."));
            if (!TranslationTools.IsSupportedLang(dto.Lang))
                return BadRequest(ApiResponse<bool>.Fail("Langue non prise en charge (ar ou wo)."));

            var rev = new TranslationReviewer
            {
                Name = name,
                Lang = dto.Lang,
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            _context.TranslationReviewers.Add(rev);
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<object>.Ok(new { rev.Id, rev.Name, rev.Lang, rev.Token }, "Lien créé."));
        }

        [HttpGet("reviewers")]
        public async Task<IActionResult> Reviewers(CancellationToken ct)
        {
            var list = await _context.TranslationReviewers
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id, r.Name, r.Lang, r.Token, r.IsActive, r.CreatedAt, r.LastSeenAt,
                    proposals = r.Proposals.Count(p => p.Text != ""),
                })
                .ToListAsync(ct);
            return Ok(ApiResponse<object>.Ok(list, "OK"));
        }

        /// <summary>Révoque (ou réactive) un lien de relecture.</summary>
        [HttpPut("reviewers/{id}/active")]
        public async Task<IActionResult> SetActive(int id, [FromQuery] bool active, CancellationToken ct)
        {
            var rev = await _context.TranslationReviewers.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rev == null) return NotFound(ApiResponse<bool>.Fail("Relecteur inconnu."));
            rev.IsActive = active;
            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<bool>.Ok(true, active ? "Lien réactivé." : "Lien révoqué."));
        }

        /// <summary>
        /// Propositions reçues, pour relecture par le SuperAdmin avant
        /// application.
        /// </summary>
        [HttpGet("proposals")]
        public async Task<IActionResult> Proposals([FromQuery] string lang = "ar",
            [FromQuery] bool onlyPending = true, CancellationToken ct = default)
        {
            if (!TranslationTools.IsSupportedLang(lang))
                return BadRequest(ApiResponse<bool>.Fail("Langue non prise en charge."));

            var q = _context.TranslationProposals
                .Where(p => p.Lang == lang && p.Text != "");
            if (onlyPending) q = q.Where(p => !p.IsApplied);

            var list = await q
                .OrderBy(p => p.TranslationKey.Key)
                .Select(p => new
                {
                    p.Id,
                    key = p.TranslationKey.Key,
                    fr = p.TranslationKey.Fr,
                    current = lang == "ar" ? p.TranslationKey.Ar : p.TranslationKey.Wo,
                    proposal = p.Text,
                    p.Note,
                    reviewer = p.Reviewer.Name,
                    p.IsApplied,
                    p.UpdatedAt,
                })
                .Take(3000)
                .ToListAsync(ct);

            return Ok(ApiResponse<object>.Ok(list, $"{list.Count} proposition(s)."));
        }

        /// <summary>
        /// Produit le fichier de langue complet, propositions incluses — prêt à
        /// remplacer `ar.json` ou `wo.json` dans le dépôt frontend.
        ///
        /// C'est le livrable de l'outil : les corrections ne « s'appliquent » pas
        /// toutes seules en production, elles passent par le dépôt, la
        /// régénération des traductions compilées (§155) et un déploiement — donc
        /// par une relecture et un test.
        /// </summary>
        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] string lang = "ar",
            [FromQuery] bool includeProposals = true, CancellationToken ct = default)
        {
            if (!TranslationTools.IsSupportedLang(lang))
                return BadRequest(ApiResponse<bool>.Fail("Langue non prise en charge."));

            var keys = await _context.TranslationKeys
                .Where(k => !k.IsObsolete)
                .OrderBy(k => k.Id)
                .Select(k => new
                {
                    k.Id,
                    k.Key,
                    current = lang == "ar" ? k.Ar : k.Wo,
                    // La proposition la plus récente pour cette langue.
                    proposal = _context.TranslationProposals
                        .Where(p => p.TranslationKeyId == k.Id && p.Lang == lang && p.Text != "")
                        .OrderByDescending(p => p.UpdatedAt)
                        .Select(p => p.Text)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var entries = keys.Select(k => new KeyValuePair<string, string>(
                k.Key,
                includeProposals
                    ? TranslationTools.Resolve(k.current, k.proposal)
                    : k.current ?? string.Empty));

            var json = TranslationTools.ToJson(entries);
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"{lang}.json");
        }

        /// <summary>Marque des propositions comme reprises dans l'application.</summary>
        [HttpPost("proposals/mark-applied")]
        public async Task<IActionResult> MarkApplied([FromQuery] string lang = "ar", CancellationToken ct = default)
        {
            if (!TranslationTools.IsSupportedLang(lang))
                return BadRequest(ApiResponse<bool>.Fail("Langue non prise en charge."));

            var now = DateTime.UtcNow;
            var pending = await _context.TranslationProposals
                .Where(p => p.Lang == lang && p.Text != "" && !p.IsApplied)
                .ToListAsync(ct);
            foreach (var p in pending) { p.IsApplied = true; p.AppliedAt = now; }
            await _context.SaveChangesAsync(ct);

            return Ok(ApiResponse<int>.Ok(pending.Count, $"{pending.Count} proposition(s) marquée(s) comme reprises."));
        }
    }
}
