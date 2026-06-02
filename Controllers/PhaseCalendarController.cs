

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;

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
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [RequireMenu("PhaseCalendar.Index")]
        public async Task<IActionResult> List()
        {
            var phases = await _context.ProjectPhases
                .Include(x => x.Project)
                .Where(x => x.PlanEnd != null)
                .OrderBy(x => x.PlanEnd)
                .Select(x => new
                {
                    id = x.PhaseId,
                    title = ((x.Project != null ? x.Project.ProjectName : "-") ?? "-") + "\n" + (x.PhaseName ?? "-"),
                    start = x.PlanEnd,
                    allDay = true,

                    backgroundColor =
                        x.PhaseStatus == "วางแผน" ? "#9ca3af" :
                        x.PhaseStatus == "กำลังดำเนินการ" ? "#facc15" :
                        x.PhaseStatus == "ส่งงวดงานแล้ว" ? "#22c55e" :
                        x.PhaseStatus == "อนุมัติแล้ว" ? "#22c55e" :
                        x.PhaseStatus == "ตีกลับ" ? "#ef4444" :
                        "#6b7280",

                    borderColor =
                        x.PhaseStatus == "วางแผน" ? "#9ca3af" :
                        x.PhaseStatus == "กำลังดำเนินการ" ? "#facc15" :
                        x.PhaseStatus == "ส่งงวดงานแล้ว" ? "#22c55e" :
                        x.PhaseStatus == "อนุมัติแล้ว" ? "#22c55e" :
                        x.PhaseStatus == "ตีกลับ" ? "#ef4444" :
                        "#6b7280",

                    extendedProps = new
                    {
                        projectId = x.ProjectId,
                        projectName = x.Project != null ? x.Project.ProjectName : "-",
                        phaseName = x.PhaseName,
                        phaseStatus = x.PhaseStatus,
                        periodStartDate = x.PeriodStartDate,
                        periodEndDate = x.PeriodEndDate,
                        planEnd = x.PlanEnd
                    }
                })
                .ToListAsync();

            return Json(phases);
        }
    }
}
