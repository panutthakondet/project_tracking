using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    [RequireMenu("Followups.Index")]
    public class FollowupsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly ILogger<FollowupsController> _logger;
        private static readonly CultureInfo ThaiCulture = new("th-TH");
        private static readonly string[] FollowupStatuses = { "OPEN", "DONE", "ACK" };

        public FollowupsController(
            AppDbContext context,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            ILogger<FollowupsController> logger)
        {
            _context = context;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _logger = logger;
        }

        // ===== Follow-up Dashboard =====
        [RequireMenu("Followups.Dashboard")]
        public async Task<IActionResult> Dashboard(int? ownerEmpId, string? owner, string? status)
        {
            var today = DateTime.Today;
            owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();
            var selectedStatus = (status ?? "").Trim().ToUpperInvariant();
            selectedStatus = FollowupStatuses.Contains(selectedStatus) ? selectedStatus : "";

            if (!ownerEmpId.HasValue && !string.IsNullOrWhiteSpace(owner))
            {
                ownerEmpId = await _context.Employees
                    .AsNoTracking()
                    .Where(e => e.EmpName == owner)
                    .Select(e => (int?)e.EmpId)
                    .FirstOrDefaultAsync();
            }

            var ownerIds = await _context.ProjectFollowups
                .AsNoTracking()
                .Where(x => x.OwnerEmpId.HasValue)
                .Select(x => x.OwnerEmpId!.Value)
                .Distinct()
                .ToListAsync();

            ViewBag.OwnerEmployees = await _context.Employees
                .AsNoTracking()
                .Where(e => ownerIds.Contains(e.EmpId))
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            var query = _context.ProjectFollowups
                .Include(x => x.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(x => x.Owner)
                    .ThenInclude(owner => owner!.LoginUser)
                .Include(x => x.CreatedByEmployee)
                .Where(x => FollowupStatuses.Contains(x.Status))
                .AsQueryable();

            if (ownerEmpId.HasValue)
            {
                query = query.Where(x => x.OwnerEmpId == ownerEmpId.Value);
            }

            if (!string.IsNullOrWhiteSpace(selectedStatus))
            {
                query = query.Where(x => x.Status == selectedStatus);
            }

            var data = await query
                .OrderBy(x => x.Status == "OPEN" ? 0 : x.Status == "DONE" ? 1 : x.Status == "ACK" ? 2 : 3)
                .ThenBy(x => x.NextFollowupDate ?? DateTime.MaxValue)
                .ThenBy(x => x.Project != null && x.Project.Coop != null ? x.Project.Coop.CoopName : "")
                .ThenBy(x => x.Project != null ? x.Project.ProjectName : "")
                .Select(x => new
                {
                    x.FollowupId,
                    ProjectId = x.ProjectId,
                    CoopName = x.Project != null && x.Project.Coop != null
                        ? x.Project.Coop.CoopName
                        : "",
                    Project = x.Project != null
                        ? ((x.Project.Coop != null ? x.Project.Coop.CoopName + " - " : "") + x.Project.ProjectName)
                        : "",
                    x.TaskTitle,
                    x.PartnerName,
                    x.OwnerEmpId,
                    Owner = x.Owner != null ? x.Owner.EmpName : "",
                    OwnerAvatar = x.Owner != null && x.Owner.LoginUser != null
                        ? x.Owner.LoginUser.ProfileImagePath
                        : null,
                    NextFollowupDate = x.NextFollowupDate ?? today,
                    Status = x.Status,
                    DateStatus =
                        x.NextFollowupDate == null ? "Done" :
                        x.NextFollowupDate < today ? "Overdue" :
                        x.NextFollowupDate == today ? "Today" :
                        "Upcoming"
                })
                .ToListAsync();

            ViewBag.SelectedOwnerEmpId = ownerEmpId;
            ViewBag.StatusList = FollowupStatuses;
            ViewBag.SelectedStatus = selectedStatus ?? "";
            return View(data);
        }

        public async Task<IActionResult> Index(string? coopName, int? projectId, int? ownerEmpId, string? status)
        {
            var selectedCoopName = string.IsNullOrWhiteSpace(coopName) ? "" : coopName.Trim();
            var selectedStatus = (status ?? "").Trim().ToUpperInvariant();
            selectedStatus = FollowupStatuses.Contains(selectedStatus) ? selectedStatus : "";

            ViewBag.Coops = await _context.CntMCoops
                .AsNoTracking()
                .Where(x => x.CoopName != null && x.CoopName != "")
                .OrderBy(x => x.CoopName)
                .Select(x => x.CoopName)
                .ToListAsync();

            var projectDropdownQuery = _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(selectedCoopName))
            {
                projectDropdownQuery = projectDropdownQuery.Where(p => p.Coop != null && p.Coop.CoopName == selectedCoopName);
            }

            // send project list to dropdown
            ViewBag.Projects = await projectDropdownQuery
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var ownerIds = await _context.ProjectFollowups
                .AsNoTracking()
                .Where(x => x.OwnerEmpId.HasValue)
                .Select(x => x.OwnerEmpId!.Value)
                .Distinct()
                .ToListAsync();

            ViewBag.OwnerEmployees = await _context.Employees
                .AsNoTracking()
                .Where(e => ownerIds.Contains(e.EmpId))
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.SelectedCoopName = selectedCoopName;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedOwnerEmpId = ownerEmpId;
            ViewBag.StatusList = FollowupStatuses;
            ViewBag.SelectedStatus = selectedStatus;

            var query = _context.ProjectFollowups.AsQueryable();

            if (!string.IsNullOrWhiteSpace(selectedCoopName))
            {
                query = query.Where(x => x.Project != null && x.Project.Coop != null && x.Project.Coop.CoopName == selectedCoopName);
            }

            // filter by project
            if (projectId != null)
            {
                query = query.Where(x => x.ProjectId == projectId);
            }

            if (ownerEmpId != null)
            {
                query = query.Where(x => x.OwnerEmpId == ownerEmpId);
            }

            if (!string.IsNullOrWhiteSpace(selectedStatus))
            {
                query = query.Where(x => x.Status == selectedStatus);
            }

            var data = await query
                .Include(x => x.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(x => x.Owner)
                    .ThenInclude(owner => owner!.LoginUser)
                .Include(x => x.CreatedByEmployee)
                .OrderBy(x => x.Status == "OPEN" ? 0 : x.Status == "DONE" ? 1 : x.Status == "ACK" ? 2 : 3)
                .ThenBy(x => x.NextFollowupDate ?? DateTime.MaxValue)
                .ThenBy(x => x.Project != null && x.Project.Coop != null ? x.Project.Coop.CoopName : "")
                .ThenBy(x => x.Project != null ? x.Project.ProjectName : "")
                .ToListAsync();

            return View(data);
        }

        [RequireMenu("Followups.Index")]
        public async Task<IActionResult> ViewOnly(int? projectId, string? owner, string? status, int? departmentId)
        {
            departmentId = await ReportDepartmentSupport.LoadAsync(this, _context, departmentId);
            var projectQuery = _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .AsQueryable();
            if (departmentId.HasValue)
                projectQuery = projectQuery.Where(p => p.DepartmentId == departmentId.Value);
            var projects = await projectQuery
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var ownerList = await _context.ProjectFollowups
                .AsNoTracking()
                .Include(f => f.Owner)
                .Where(f => f.Owner != null && (!departmentId.HasValue || (f.Project != null && f.Project.DepartmentId == departmentId.Value)))
                .Select(f => f.Owner!.EmpName)
                .Distinct()
                .OrderBy(name => name)
                .ToListAsync();

            var query = _context.ProjectFollowups
                .AsNoTracking()
                .Include(f => f.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(f => f.Owner)
                .Include(f => f.CreatedByEmployee)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(f => f.ProjectId == projectId.Value);
            if (departmentId.HasValue)
                query = query.Where(f => f.Project != null && f.Project.DepartmentId == departmentId.Value);

            if (!string.IsNullOrWhiteSpace(owner))
                query = query.Where(f => f.Owner != null && f.Owner.EmpName == owner);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(f => f.Status == status);

            var followups = await query
                .OrderBy(f => f.NextFollowupDate ?? DateTime.MaxValue)
                .ThenByDescending(f => f.CreatedAt)
                .ThenBy(f => f.FollowupId)
                .ToListAsync();

            ViewBag.Projects = projects;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.OwnerList = ownerList;
            ViewBag.SelectedOwner = owner ?? "";
            ViewBag.StatusList = FollowupStatuses;
            ViewBag.SelectedStatus = status ?? "";

            return View(followups
                .OrderBy(f => f.Project?.Coop?.CoopName ?? "")
                .ThenBy(f => f.Project?.ProjectName ?? "")
                .ThenBy(f => f.NextFollowupDate ?? DateTime.MaxValue)
                .ThenByDescending(f => f.CreatedAt)
                .ToList());
        }

        [RequireMenu("Followups.Create")]
        public async Task<IActionResult> Create(int? projectId)
        {
            var employees = await _context.Employees
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.Employees = employees ?? new List<Employee>();
            ViewBag.CurrentEmployeeName = await GetCurrentEmployeeNameAsync();

            if (projectId != null)
            {
                var project = await _context.Projects
                    .Include(p => p.Coop)
                    .FirstOrDefaultAsync(p => p.ProjectId == projectId);

                if (project != null)
                {
                    ViewBag.ProjectName = project.ProjectDisplayName;
                    ViewBag.ProjectId = project.ProjectId;
                }
            }

            return View();
        }

        [HttpPost]
        [RequireMenu("Followups.Create")]
        public async Task<IActionResult> Create(ProjectFollowup model)
        {
            ApplyFollowupDateInput(model, nameof(ProjectFollowup.NextFollowupDate));

            if (ModelState.IsValid)
            {
                model.Status = NormalizeFollowupStatus(model.Status);
                model.CreatedByEmpId = await GetCurrentEmpIdAsync();
                if (model.CreatedAt == default)
                    model.CreatedAt = DateTime.Now;

                _context.ProjectFollowups.Add(model);
                await _context.SaveChangesAsync();

                await SendFollowupCreatedToOwnerSafelyAsync(model.FollowupId);
                return RedirectToAction("Index", new { projectId = model.ProjectId });
            }

            var employees = await _context.Employees
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.Employees = employees ?? new List<Employee>();
            ViewBag.CurrentEmployeeName = await GetCurrentEmployeeNameAsync();
            ViewBag.ProjectName = await GetProjectDisplayNameAsync(model.ProjectId);
            ViewBag.ProjectId = model.ProjectId;

            return View(model);
        }

        private void ApplyFollowupDateInput(ProjectFollowup model, string propertyName)
        {
            ModelState.Remove(propertyName);

            var raw = Request.Form[propertyName].ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                model.NextFollowupDate = null;
                return;
            }

            if (TryParseThaiDate(raw, out var parsedDate))
            {
                model.NextFollowupDate = parsedDate;
                return;
            }

            model.NextFollowupDate = null;
            ModelState.AddModelError(propertyName, "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
        }

        private void ApplyFollowupLogDateInput(ProjectFollowupLog log, string propertyName)
        {
            ModelState.Remove(propertyName);

            var raw = Request.Form[propertyName].ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                log.NextFollowupDate = null;
                return;
            }

            if (TryParseThaiDate(raw, out var parsedDate))
            {
                log.NextFollowupDate = parsedDate;
                return;
            }

            log.NextFollowupDate = null;
            ModelState.AddModelError(propertyName, "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
        }

        private static bool TryParseThaiDate(string value, out DateTime date)
        {
            date = default;
            var parts = (value ?? "").Trim().Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 ||
                !int.TryParse(parts[0], out var day) ||
                !int.TryParse(parts[1], out var month) ||
                !int.TryParse(parts[2], out var year))
            {
                return false;
            }

            if (year < 100) year += 2500;
            if (year > 2400) year -= 543;

            try
            {
                date = new DateTime(year, month, day);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ===== Edit Follow-up =====
        [RequireMenu("Followups.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var followup = await _context.ProjectFollowups
                .Include(x => x.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(x => x.CreatedByEmployee)
                .FirstOrDefaultAsync(x => x.FollowupId == id);

            var employees = await _context.Employees
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.Employees = employees ?? new List<Employee>();

            if (followup == null)
                return NotFound();

            ViewBag.ProjectName = followup.Project?.ProjectDisplayName;
            ViewBag.ProjectId = followup.ProjectId;
            ViewBag.StatusList = FollowupStatuses;

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

                ViewBag.ProjectName = await GetProjectDisplayNameAsync(model.ProjectId);

                ViewBag.ProjectId = model.ProjectId;
                ViewBag.StatusList = FollowupStatuses;

                return View(model);
            }

            var followup = await _context.ProjectFollowups
                .FirstOrDefaultAsync(x => x.FollowupId == model.FollowupId);

            if (followup == null)
                return NotFound();

            var oldStatus = NormalizeFollowupStatus(followup.Status);
            var newStatus = NormalizeFollowupStatus(model.Status);
            var shouldNotifyOwnerAck = oldStatus != "ACK" && newStatus == "ACK";

            followup.TaskTitle = model.TaskTitle;
            followup.PartnerName = model.PartnerName;
            followup.OwnerEmpId = model.OwnerEmpId;
            followup.NextFollowupDate = model.NextFollowupDate;
            followup.Status = newStatus;

            await _context.SaveChangesAsync();

            if (shouldNotifyOwnerAck)
                await SendFollowupAckToOwnerSafelyAsync(followup.FollowupId);

            return RedirectToAction("Index", new { projectId = followup.ProjectId });
        }

        // ===== Follow-up Detail + History =====
        [RequireMenu("Followups.Details")]
        public async Task<IActionResult> Details(int id)
        {
            var followup = await _context.ProjectFollowups
                .Include(x => x.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(x => x.Owner)
                    .ThenInclude(owner => owner!.LoginUser)
                .Include(x => x.CreatedByEmployee)
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
            ApplyFollowupLogDateInput(log, nameof(ProjectFollowupLog.NextFollowupDate));

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

            await SendFollowupOwnerUpdateToRequesterSafelyAsync(
                followup.FollowupId,
                "แจ้ง Follow-up Log:",
                log.ContactType,
                log.Note,
                log.NextFollowupDate);

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

            await SendFollowupOwnerUpdateToRequesterSafelyAsync(
                followup.FollowupId,
                "แจ้ง Follow-up Log:",
                log.ContactType,
                log.Note,
                log.NextFollowupDate);

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

            await SendFollowupOwnerUpdateToRequesterSafelyAsync(
                followup.FollowupId,
                "แจ้ง Follow-up Log:",
                log.ContactType,
                log.Note,
                log.NextFollowupDate);

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
                TempData["FollowupMessage"] = "ไม่สามารถกดเสร็จแล้วได้ เนื่องจากรายการนี้เสร็จแล้วหรือรับทราบแล้ว";
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

            await SendFollowupOwnerUpdateToRequesterSafelyAsync(
                followup.FollowupId,
                "แจ้ง Follow-up เสร็จแล้ว:",
                log.ContactType,
                log.Note,
                log.NextFollowupDate);

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

        private async Task SendFollowupCreatedToOwnerSafelyAsync(int followupId)
        {
            await SendFollowupNotificationSafelyAsync(
                followupId,
                recipientSelector: followup => followup.OwnerEmpId,
                lineFeature: LineNotificationFeatures.FollowupsCreate,
                telegramFeature: TelegramNotificationFeatures.FollowupsCreate,
                title: "แจ้ง Follow-up ใหม่:",
                eventText: "สร้าง Follow-up ใหม่",
                contactType: null,
                note: null,
                nextFollowupDate: null,
                logMessage: "Send created follow-up notification failed. FollowupId={FollowupId}");
        }

        private async Task SendFollowupAckToOwnerSafelyAsync(int followupId)
        {
            await SendFollowupNotificationSafelyAsync(
                followupId,
                recipientSelector: followup => followup.OwnerEmpId,
                lineFeature: LineNotificationFeatures.FollowupsAck,
                telegramFeature: TelegramNotificationFeatures.FollowupsAck,
                title: "แจ้ง Follow-up รับทราบแล้ว:",
                eventText: "ผู้สั่งงานรับทราบงานติดตามแล้ว",
                contactType: null,
                note: null,
                nextFollowupDate: null,
                logMessage: "Send ACK follow-up notification failed. FollowupId={FollowupId}");
        }

        private async Task SendFollowupOwnerUpdateToRequesterSafelyAsync(
            int followupId,
            string title,
            string? contactType,
            string? note,
            DateTime? nextFollowupDate)
        {
            await SendFollowupNotificationSafelyAsync(
                followupId,
                recipientSelector: followup => followup.CreatedByEmpId,
                lineFeature: LineNotificationFeatures.FollowupsOwnerUpdate,
                telegramFeature: TelegramNotificationFeatures.FollowupsOwnerUpdate,
                title: title,
                eventText: contactType == "Done" ? "Owner ปิดงาน Done แล้ว" : "Owner บันทึก Log แล้ว",
                contactType: contactType,
                note: note,
                nextFollowupDate: nextFollowupDate,
                logMessage: "Send owner update follow-up notification failed. FollowupId={FollowupId}");
        }

        private async Task SendFollowupNotificationSafelyAsync(
            int followupId,
            Func<ProjectFollowup, int?> recipientSelector,
            string lineFeature,
            string telegramFeature,
            string title,
            string eventText,
            string? contactType,
            string? note,
            DateTime? nextFollowupDate,
            string logMessage)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(lineFeature, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(telegramFeature, HttpContext.RequestAborted);

            if (!sendLine && !sendTelegram)
                return;

            try
            {
                var followup = await _context.ProjectFollowups
                    .AsNoTracking()
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.Coop)
                    .Include(x => x.Owner)
                    .Include(x => x.CreatedByEmployee)
                    .FirstOrDefaultAsync(x => x.FollowupId == followupId);

                if (followup == null)
                    return;

                var recipientEmpId = recipientSelector(followup);
                if (!recipientEmpId.HasValue || recipientEmpId.Value <= 0)
                    return;

                var message = BuildFollowupNotificationMessage(
                    followup,
                    eventText,
                    contactType,
                    note,
                    nextFollowupDate);
                var targetUrl = $"/Followups/Details/{followup.FollowupId}";

                await SendChatNotificationToEmployeeSafelyAsync(
                    recipientEmpId.Value,
                    title,
                    message,
                    targetUrl,
                    sendLine,
                    sendTelegram,
                    "follow-up",
                    followup.FollowupId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, logMessage, followupId);
            }
        }

        private async Task SendChatNotificationToEmployeeSafelyAsync(
            int empId,
            string title,
            string message,
            string targetUrl,
            bool sendLine,
            bool sendTelegram,
            string context,
            int sourceId)
        {
            if (sendLine)
            {
                try
                {
                    await _lineMessagingService.SendNotificationToEmployeeAsync(
                        empId,
                        title,
                        message,
                        targetUrl,
                        HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LINE notification failed. Context={Context}, SourceId={SourceId}, EmpId={EmpId}", context, sourceId, empId);
                }
            }

            if (sendTelegram)
            {
                try
                {
                    await _telegramMessagingService.SendNotificationToEmployeeAsync(
                        empId,
                        title,
                        message,
                        targetUrl,
                        HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telegram notification failed. Context={Context}, SourceId={SourceId}, EmpId={EmpId}", context, sourceId, empId);
                }
            }
        }

        private static string BuildFollowupNotificationMessage(
            ProjectFollowup followup,
            string eventText,
            string? contactType,
            string? note,
            DateTime? nextFollowupDate)
        {
            var project = followup.Project;
            var rows = new List<string>
            {
                $"เหตุการณ์: {TextOrDash(eventText)}",
                $"สหกรณ์: {TextOrDash(project?.Coop?.CoopName)}",
                $"Project: {ProjectNameForNotification(project)}",
                $"Follow-up: {TextOrDash(followup.TaskTitle)}",
                $"คู่ติดต่อ: {TextOrDash(followup.PartnerName)}",
                $"ผู้สั่งงาน: {TextOrDash(followup.CreatedByEmployee?.EmpName)}",
                $"Owner: {TextOrDash(followup.Owner?.EmpName)}",
                $"สถานะ: {FollowupStatusText(followup.Status)}",
                $"Next Follow-up: {DateText(nextFollowupDate ?? followup.NextFollowupDate)}"
            };

            if (!string.IsNullOrWhiteSpace(contactType))
                rows.Add($"ประเภท Log: {TextOrDash(contactType)}");

            if (!string.IsNullOrWhiteSpace(note))
                rows.Add($"หมายเหตุ: {TextOrDash(note)}");

            return string.Join("\n", rows);
        }

        private async Task<int?> GetCurrentEmpIdAsync()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue) return null;

            var empId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.LoginUserId == userId.Value)
                .Select(e => (int?)e.EmpId)
                .FirstOrDefaultAsync();

            if (empId.HasValue) return empId;

            return await _context.LoginUsers
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.EmpId)
                .FirstOrDefaultAsync();
        }

        private async Task<string?> GetCurrentEmployeeNameAsync()
        {
            var empId = await GetCurrentEmpIdAsync();
            if (!empId.HasValue) return null;

            return await _context.Employees
                .AsNoTracking()
                .Where(e => e.EmpId == empId.Value)
                .Select(e => e.EmpName)
                .FirstOrDefaultAsync();
        }

        private static string NormalizeFollowupStatus(string? status)
        {
            var normalized = (status ?? "OPEN").Trim().ToUpperInvariant();
            if (normalized == "IN_PROGRESS")
                return "OPEN";
            return FollowupStatuses.Contains(normalized) ? normalized : "OPEN";
        }

        private static string FollowupStatusText(string? status)
        {
            return (status ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "OPEN" => "เปิดงาน",
                "DONE" => "เสร็จแล้ว",
                "ACK" => "รับทราบแล้ว",
                _ => TextOrDash(status)
            };
        }

        private static string ProjectNameForNotification(Project? project)
            => string.IsNullOrWhiteSpace(project?.ProjectName) ? "-" : project.ProjectName.Trim();

        private static string TextOrDash(string? value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

        private static string DateText(DateTime? value)
            => value.HasValue ? value.Value.ToString("dd MMM yyyy", ThaiCulture) : "-";

        private async Task<string?> GetProjectDisplayNameAsync(int? projectId)
        {
            if (!projectId.HasValue)
                return null;

            var project = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId.Value);

            return project?.ProjectDisplayName;
        }
    }
}
