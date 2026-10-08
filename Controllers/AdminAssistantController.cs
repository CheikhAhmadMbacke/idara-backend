using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// 🔎 <b>Ce que l'assistant n'a pas su faire</b> (2026-10-08, §304) —
    /// SuperAdmin seul. C'est la feuille de route de l'assistant : les questions
    /// restées sans réponse disent quel outil ou quel sujet du guide manque ;
    /// les refus disent ce que les écoles cherchent ailleurs ; les demandes
    /// transmises au support disent ce qu'il n'a pas pu résoudre.
    /// </summary>
    [ApiController]
    [Route("api/admin/assistant")]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    public class AdminAssistantController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdminAssistantController(AppDbContext db) => _db = db;

        [HttpGet("insights")]
        public async Task<IActionResult> Insights([FromQuery] int days = 30, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 1, 180);
            var since = DateTime.UtcNow.AddDays(-days);
            var turns = _db.AssistantTurns.Where(t => t.CreatedAt >= since);

            var totals = await turns
                .GroupBy(t => 1)
                .Select(g => new
                {
                    exchanges = g.Count(),
                    succeeded = g.Count(t => t.Success),
                    charged = g.Sum(t => t.ChargedCommands),
                    included = g.Sum(t => t.IncludedCommands),
                    unanswered = g.Count(t => t.UnansweredTopic != null),
                    cost_centimes = g.Sum(t => t.CostCentimes),
                    schools = g.Select(t => t.SchoolId).Distinct().Count(),
                })
                .FirstOrDefaultAsync(ct);

            var declined = await turns
                .Where(t => t.BlockedReason != null)
                .GroupBy(t => t.BlockedReason!)
                .Select(g => new { reason = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            var unanswered = await turns
                .Where(t => t.UnansweredTopic != null)
                .OrderByDescending(t => t.CreatedAt).Take(100)
                .Select(t => new
                {
                    at = t.CreatedAt,
                    school = t.School.Name,
                    topic = t.UnansweredTopic,
                    prompt = t.Prompt,
                })
                .ToListAsync(ct);

            var refused = await turns
                .Where(t => t.BlockedReason != null && t.BlockedReason.StartsWith("declined:"))
                .OrderByDescending(t => t.CreatedAt).Take(50)
                .Select(t => new { at = t.CreatedAt, school = t.School.Name, reason = t.BlockedReason, prompt = t.Prompt })
                .ToListAsync(ct);

            var escalations = await _db.AssistantActions
                .Where(a => a.Kind == Enums.AssistantActionKind.ContactSupport && a.CreatedAt >= since)
                .OrderByDescending(a => a.CreatedAt).Take(50)
                .Select(a => new
                {
                    at = a.CreatedAt,
                    school = a.School.Name,
                    status = a.Status.ToString(),
                    result = a.ResultMessage,
                })
                .ToListAsync(ct);

            var byKind = await _db.AssistantActions
                .Where(a => a.CreatedAt >= since)
                .GroupBy(a => new { a.Kind, a.Status })
                .Select(g => new { kind = g.Key.Kind.ToString(), status = g.Key.Status.ToString(), count = g.Count() })
                .ToListAsync(ct);

            return Ok(ApiResponse<object>.Ok(new
            {
                days, totals, declined, unanswered, refused, escalations, actions = byKind,
            }, "OK"));
        }
    }
}
