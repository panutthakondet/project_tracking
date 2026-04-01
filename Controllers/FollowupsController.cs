using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    [RequireMenu("Followups.Index")]
    public class FollowupsController : Controller
    {
        private readonly AppDbContext _context;

        public FollowupsController(AppDbContext context)
        {
            _context = context;
        }

        // ===== Follow-up Dashboard =====
        [RequireMenu("Followups.Dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            var today = DateTime.Today;

            var data = await _context.ProjectFollowups
                .Include(x => x.Project)
                .Include(x => x.Owner)
                .Where(x => x.Status == "OPEN"|| x.Status == "IN_PROGRESS")
                .OrderBy(x => x.NextFollowupDate)
                .Select(x => new
                {
                    x.FollowupId,
                    ProjectId = x.ProjectId,
                    Project = x.Project != null ? x.Project.ProjectName : "",
                    x.TaskTitle,
                    x.PartnerName,
                    Owner = x.Owner != null ? x.Owner.EmpName : "",
                    NextFollowupDate = x.NextFollowupDate ?? today,
                    Status =
                        x.NextFollowupDate == null ? "Done" :
                        x.NextFollowupDate < today ? "Overdue" :
                        x.NextFollowupDate == today ? "Today" :
                        "Upcoming"
                })
                .ToListAsync();

            return View(data);
        }

        // ===== Follow-up Dashboard DONE (Waiting ACK) =====
        [RequireMenu("Followups.DashboardDone")]
        public async Task<IActionResult> DashboardDone()
        {
            var today = DateTime.Today;

            var data = await _context.ProjectFollowups
                .Include(x => x.Project)
                .Include(x => x.Owner)
                .Where(x => x.Status == "DONE")
                .OrderBy(x => x.NextFollowupDate)
                .Select(x => new
                {
                    x.FollowupId,
                    ProjectId = x.ProjectId,
                    Project = x.Project != null ? x.Project.ProjectName : "",
                    x.TaskTitle,
                    x.PartnerName,
                    Owner = x.Owner != null ? x.Owner.EmpName : "",
                    NextFollowupDate = x.NextFollowupDate ?? today,
                    Status =
                        x.NextFollowupDate == null ? "Done" :
                        x.NextFollowupDate < today ? "Overdue" :
                        x.NextFollowupDate == today ? "Today" :
                        "Upcoming"
                })
                .ToListAsync();

            return View(data);
        }

        // ===== Follow-up Dashboard ACK (Closed / Acknowledged) =====
        [RequireMenu("Followups.DashboardACK")]
        public async Task<IActionResult> DashboardACK()
        {
            var today = DateTime.Today;
            var fromDate = today.AddMonths(-1);

            var data = await _context.ProjectFollowups
                .Include(x => x.Project)
                .Include(x => x.Owner)
                .Where(x => x.Status == "ACK" && x.LastContactDate != null && x.LastContactDate >= fromDate)
                .OrderByDescending(x => x.LastContactDate)
                .Select(x => new
                {
                    x.FollowupId,
                    ProjectId = x.ProjectId,
                    Project = x.Project != null ? x.Project.ProjectName : "",
                    x.TaskTitle,
                    x.PartnerName,
                    Owner = x.Owner != null ? x.Owner.EmpName : "",
                    NextFollowupDate = x.NextFollowupDate ?? today,
                    Status = "ACK"
                })
                .ToListAsync();

            return View(data);
        }

        public async Task<IActionResult> Index(int? projectId)
        {
            // send project list to dropdown
            ViewBag.Projects = await _context.Projects
                .OrderBy(p => p.ProjectName)
                .ToListAsync();

            var query = _context.ProjectFollowups.AsQueryable();

            // filter by project
            if (projectId != null)
            {
                query = query.Where(x => x.ProjectId == projectId);
            }

            var data = await query
                .Include(x => x.Project)
                .Include(x => x.Owner)
                .OrderBy(x => x.NextFollowupDate)
                .ToListAsync();

            return View(data);
        }

        [RequireMenu("Followups.Create")]
        public async Task<IActionResult> Create(int? projectId)
        {
            var employees = await _context.Employees
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.Employees = employees ?? new List<Employee>();

            if (projectId != null)
            {
                var project = await _context.Projects
                    .FirstOrDefaultAsync(p => p.ProjectId == projectId);

                if (project != null)
                {
                    ViewBag.ProjectName = project.ProjectName;
                    ViewBag.ProjectId = project.ProjectId;
                }
            }

            return View();
        }

        [HttpPost]
        [RequireMenu("Followups.Create")]
        public async Task<IActionResult> Create(ProjectFollowup model)
        {
            if (ModelState.IsValid)
            {
                _context.ProjectFollowups.Add(model);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index", new { projectId = model.ProjectId });
            }

            var employees = await _context.Employees
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.Employees = employees ?? new List<Employee>();

            return View(model);
        }

        // ===== Edit Follow-up =====
        [RequireMenu("Followups.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var followup = await _context.ProjectFollowups
                .Include(x => x.Project)
                .FirstOrDefaultAsync(x => x.FollowupId == id);

            var employees = await _context.Employees
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.Employees = employees ?? new List<Employee>();

            if (followup == null)
                return NotFound();

            ViewBag.ProjectName = followup.Project?.ProjectName;
            ViewBag.ProjectId = followup.ProjectId;

            return View(followup);
        }

        [HttpPost]
        [RequireMenu("Followups.Edit")]
        public async Task<IActionResult> Edit(ProjectFollowup model)
        {
            if (!ModelState.IsValid)
            {
                var employees = await _context.Employees
                    .OrderBy(e => e.EmpName)
                    .ToListAsync();

                ViewBag.Employees = employees ?? new List<Employee>();

                ViewBag.ProjectName = (await _context.Projects
                    .Where(p => p.ProjectId == model.ProjectId)
                    .Select(p => p.ProjectName)
                    .FirstOrDefaultAsync());

                ViewBag.ProjectId = model.ProjectId;

                return View(model);
            }

            var followup = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == model.FollowupId);

            if (followup == null)
                return NotFound();

            followup.TaskTitle = model.TaskTitle;
            followup.PartnerName = model.PartnerName;
            followup.OwnerEmpId = model.OwnerEmpId;
            followup.NextFollowupDate = model.NextFollowupDate;
            followup.Status = model.Status;

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = followup.ProjectId });
        }

        // ===== Follow-up Detail + History =====
        [RequireMenu("Followups.Details")]
        public async Task<IActionResult> Details(int id)
        {
            var followup = await _context.ProjectFollowups
                .Include(x => x.Project)
                .Include(x => x.Owner)
                .FirstOrDefaultAsync(x => x.FollowupId == id);

            if (followup == null)
                return NotFound();

            var logs = await _context.ProjectFollowupLogs
                .Where(x => x.FollowupId == id)
                .OrderByDescending(x => x.ContactDate)
                .ToListAsync();

            ViewBag.Logs = logs;

            return View(followup);
        }

        // ===== Add Follow-up Log (Call / Email / Meeting) =====
        [HttpPost]
        [RequireMenu("Followups.Log")]
        public async Task<IActionResult> AddLog(ProjectFollowupLog log)
        {
            if (!ModelState.IsValid)
                return RedirectToAction("Details", new { id = log.FollowupId });

            log.ContactDate = DateTime.Now;

            _context.ProjectFollowupLogs.Add(log);

            var followup = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == log.FollowupId);

            if (followup == null)
                return NotFound();

            if (log.NextFollowupDate != null)
            {
                followup.NextFollowupDate = log.NextFollowupDate;
            }

            // update last contact info
            followup.LastContactDate = log.ContactDate;
            followup.LastContactType = log.ContactType;

            await _context.SaveChangesAsync();

            return RedirectToAction("Details", new { id = log.FollowupId });
        }

        // ===== Quick Log: Call =====
        [HttpPost]
        [RequireMenu("Followups.Log")]
        public async Task<IActionResult> QuickCall(int followupId)
        {
            var followup = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == followupId);

            if (followup == null)
                return NotFound();

            var log = new ProjectFollowupLog
            {
                FollowupId = followupId,
                ContactType = "Call",
                ContactDate = DateTime.Now,
                Note = "Quick Call log"
            };

            _context.ProjectFollowupLogs.Add(log);

            // update last contact
            followup.LastContactDate = log.ContactDate;
            followup.LastContactType = log.ContactType;

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = followup.ProjectId });
        }

        // ===== Quick Log: Email =====
        [HttpPost]
        [RequireMenu("Followups.Log")]
        public async Task<IActionResult> QuickEmail(int followupId)
        {
            var followup = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == followupId);

            if (followup == null)
                return NotFound();

            var log = new ProjectFollowupLog
            {
                FollowupId = followupId,
                ContactType = "Email",
                ContactDate = DateTime.Now,
                Note = "Quick Email log"
            };

            _context.ProjectFollowupLogs.Add(log);

            // update last contact
            followup.LastContactDate = log.ContactDate;
            followup.LastContactType = log.ContactType;

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = followup.ProjectId });
        }

        // ===== Mark Follow-up Done =====
        [HttpPost]
        [RequireMenu("Followups.Done")]
        public async Task<IActionResult> MarkDone(int followupId, string? Note)
        {
            var followup = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == followupId);

            if (followup == null)
                return NotFound();

            if (followup.Status == "DONE" || followup.Status == "ACK")
            {
                TempData["FollowupMessage"] = "ไม่สามารถกด DONE ได้ เนื่องจากรายการนี้ถูก DONE หรือ ACK แล้ว";
                return RedirectToAction("Index", new { projectId = followup.ProjectId });
            }

            followup.Status = "DONE";
            followup.NextFollowupDate = null;

            var log = new ProjectFollowupLog
            {
                FollowupId = followupId,
                ContactType = "Done",
                ContactDate = DateTime.Now,
                Note = string.IsNullOrEmpty(Note) ? "Follow-up completed" : Note
            };

            _context.ProjectFollowupLogs.Add(log);

            // update last contact
            followup.LastContactDate = log.ContactDate;
            followup.LastContactType = log.ContactType;

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = followup.ProjectId });
        }

        // ===== Full History Page =====
        [RequireMenu("Followups.History")]
        public async Task<IActionResult> History(int followupId)
        {
            var followup = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == followupId);

            if (followup == null)
                return NotFound();

            var logs = await _context.ProjectFollowupLogs
                .Where(x => x.FollowupId == followupId)
                .OrderByDescending(x => x.ContactDate)
                .ToListAsync();

            ViewBag.Followup = followup;
            return View(logs);
        }
        // ===== Delete Follow-up =====
        [HttpPost]
        [RequireMenu("Followups.Delete")]
        public async Task<IActionResult> Delete(int followupId)
        {
            var item = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == followupId);

            if (item == null)
                return NotFound();

            var projectId = item.ProjectId;

            _context.ProjectFollowups.Remove(item);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = projectId });
        }
    }
}