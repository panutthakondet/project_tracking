

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    [RequireMenu("PhaseCalendar.Index")]
    public class PhaseCalendarController : Controller
    {
        private readonly AppDbContext _context;

        public PhaseCalendarController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("PhaseCalendar.Index")]
        public async Task<IActionResult> Index()
        {
            ViewBag.PhaseStatuses = await _context.ProjectPhaseStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                .ToListAsync();
            return View();
        }

        [HttpGet]
        [RequireMenu("PhaseCalendar.Index")]
        public async Task<IActionResult> List()
        {
            var definitions = await _context.ProjectPhaseStatuses
                .AsNoTracking()
                .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                .ToListAsync();
            var definitionById = definitions.ToDictionary(x => x.StatusId);

            var phaseRows = await _context.ProjectPhases
                .Include(x => x.Project)
                .Include(x => x.StatusDefinition)
                .Where(x => x.PlanEnd != null)
                .OrderBy(x => x.PlanEnd)
                .Select(x => new
                {
                    x.PhaseId, x.ProjectId, x.PhaseName, x.PhaseStatus, x.StatusId,
                    StatusCode = x.StatusDefinition != null ? x.StatusDefinition.StatusCode : null,
                    StatusDesc = x.StatusDefinition != null ? x.StatusDefinition.StatusDesc : null,
                    x.PlanEnd, x.PeriodEndDate,
                    ProjectName = x.Project != null ? x.Project.ProjectName : "-"
                })
                .ToListAsync();

            var phases = phaseRows.Select(x =>
            {
                var definition = x.StatusId.HasValue && definitionById.TryGetValue(x.StatusId.Value, out var found)
                    ? found
                    : definitions.FirstOrDefault(d => string.Equals(d.StatusCode, x.StatusCode, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(d.StatusDesc, x.PhaseStatus, StringComparison.OrdinalIgnoreCase));
                var index = definition == null ? definitions.Count : definitions.IndexOf(definition);
                var color = definition == null ? "#64748b" : WorkflowStatusPresentation.Color(definition, index);
                var statusCode = definition?.StatusCode ?? x.StatusCode ?? x.PhaseStatus ?? string.Empty;
                var statusDesc = definition?.StatusDesc ?? x.StatusDesc ?? x.PhaseStatus ?? "-";
                return new
                {
                    id = x.PhaseId,
                    title = (x.ProjectName ?? "-") + "\n" + (x.PhaseName ?? "-"),
                    start = x.PlanEnd,
                    allDay = true,
                    backgroundColor = color,
                    borderColor = color,
                    extendedProps = new
                    {
                        projectId = x.ProjectId,
                        projectName = x.ProjectName,
                        phaseName = x.PhaseName,
                        phaseStatus = statusDesc,
                        phaseStatusCode = statusCode,
                        statusColor = color,
                        periodEndDate = x.PeriodEndDate,
                        planEnd = x.PlanEnd
                    }
                };
            }).ToList();

            return Json(phases);
        }
    }
}
