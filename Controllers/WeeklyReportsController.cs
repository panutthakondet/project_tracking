using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class WeeklyReportsController : BaseController
    {
        private const long MaxUploadSize = 209715200;

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public WeeklyReportsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public async Task<IActionResult> Index(string? status = null)
        {
            var userId = CurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Auth");

            var query = _context.WeeklyReports
                .AsNoTracking()
                .Include(x => x.CreatedByEmployee)
                .Include(x => x.Attachments)
                .Where(x => x.CreatedByUserId == userId.Value);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = status.Trim().ToUpperInvariant();
                query = query.Where(x => x.Status == normalizedStatus);
                ViewBag.SelectedStatus = normalizedStatus;
            }

            var reports = await query
                .OrderByDescending(x => x.UpdatedAt)
                .ToListAsync();

            return View(reports);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            var (weekStart, weekEnd) = GetDefaultWeekRange();
            var model = new WeeklyReportFormViewModel
            {
                Report = new WeeklyReport
                {
                    WeekStart = weekStart,
                    WeekEnd = weekEnd,
                    Subject = $"สรุปรายงานประจำสัปดาห์ {weekStart:dd/MM/yyyy} - {weekEnd:dd/MM/yyyy}"
                },
                PendingItems = await LoadPendingWorkAsync(identity.Employee?.EmpId),
                Users = await LoadUserOptionsAsync(excludeUserId: identity.User.UserId)
            };

            return View("Form", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadSize)]
        [RequestSizeLimit(MaxUploadSize)]
        public async Task<IActionResult> Create(WeeklyReportFormViewModel model, List<IFormFile>? files, string submitAction = "draft")
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            NormalizeReportInput(model.Report);

            if (!ValidateReport(model.Report, submitAction, model.SelectedUserIds))
            {
                model.PendingItems = await LoadPendingWorkAsync(identity.Employee?.EmpId);
                model.Users = await LoadUserOptionsAsync(excludeUserId: identity.User.UserId);
                return View("Form", model);
            }

            model.Report.CreatedByUserId = identity.User.UserId;
            model.Report.CreatedByEmpId = identity.Employee?.EmpId;
            model.Report.Status = submitAction == "send" ? "SENT_TO_PM" : "DRAFT";
            model.Report.CreatedAt = DateTime.Now;
            model.Report.UpdatedAt = DateTime.Now;

            if (submitAction == "send")
                model.Report.SentToPmAt = DateTime.Now;

            _context.WeeklyReports.Add(model.Report);
            await _context.SaveChangesAsync();

            await SaveAttachmentsAsync(model.Report.ReportId, files, identity.User.UserId);

            if (submitAction == "send")
            {
                await SendReportMessageAsync(
                    model.Report,
                    model.SelectedUserIds,
                    subjectPrefix: "ส่งรายงานสรุปประจำสัปดาห์",
                    body: model.Report.Summary,
                    nextStatus: "SENT_TO_PM");
            }

            TempData["Success"] = submitAction == "send"
                ? "ส่งรายงานให้ Project Manager แล้ว"
                : "บันทึกร่างรายงานแล้ว";

            return RedirectToAction(nameof(Details), new { id = model.Report.ReportId });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            var report = await _context.WeeklyReports
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.ReportId == id);

            if (report == null) return NotFound();
            if (!CanEditReport(report, identity.User.UserId)) return Forbid();

            var model = new WeeklyReportFormViewModel
            {
                Report = report,
                PendingItems = await LoadPendingWorkAsync(identity.Employee?.EmpId),
                Users = await LoadUserOptionsAsync(excludeUserId: identity.User.UserId)
            };

            return View("Form", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadSize)]
        [RequestSizeLimit(MaxUploadSize)]
        public async Task<IActionResult> Edit(int id, WeeklyReportFormViewModel model, List<IFormFile>? files, string submitAction = "draft")
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            var report = await _context.WeeklyReports
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.ReportId == id);

            if (report == null) return NotFound();
            if (!CanEditReport(report, identity.User.UserId)) return Forbid();

            NormalizeReportInput(model.Report);

            if (!ValidateReport(model.Report, submitAction, model.SelectedUserIds))
            {
                model.Report = report;
                model.PendingItems = await LoadPendingWorkAsync(identity.Employee?.EmpId);
                model.Users = await LoadUserOptionsAsync(excludeUserId: identity.User.UserId);
                return View("Form", model);
            }

            report.WeekStart = model.Report.WeekStart;
            report.WeekEnd = model.Report.WeekEnd;
            report.Subject = model.Report.Subject;
            report.Summary = model.Report.Summary;
            report.UpdatedAt = DateTime.Now;

            await SaveAttachmentsAsync(report.ReportId, files, identity.User.UserId);

            if (submitAction == "send")
            {
                report.Status = "SENT_TO_PM";
                report.SentToPmAt = DateTime.Now;
                await _context.SaveChangesAsync();

                await SendReportMessageAsync(
                    report,
                    model.SelectedUserIds,
                    subjectPrefix: "ส่งรายงานสรุปประจำสัปดาห์",
                    body: report.Summary,
                    nextStatus: "SENT_TO_PM");
            }
            else
            {
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = submitAction == "send"
                ? "ส่งรายงานให้ Project Manager แล้ว"
                : "บันทึกร่างรายงานแล้ว";

            return RedirectToAction(nameof(Details), new { id = report.ReportId });
        }

        public async Task<IActionResult> Details(int id)
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            var report = await _context.WeeklyReports
                .AsNoTracking()
                .Include(x => x.CreatedByEmployee)
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.ReportId == id);

            if (report == null) return NotFound();

            var canView = report.CreatedByUserId == identity.User.UserId
                || await _context.MailboxMessages
                    .AsNoTracking()
                    .Where(x => x.ReportId == id)
                    .SelectMany(x => x.Recipients)
                    .AnyAsync(x => x.RecipientUserId == identity.User.UserId)
                || IsAdmin();

            if (!canView) return Forbid();

            var model = new WeeklyReportDetailsViewModel
            {
                Report = report,
                Users = await LoadUserOptionsAsync(excludeUserId: identity.User.UserId),
                CanEditDraft = CanEditReport(report, identity.User.UserId)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Forward(int reportId, int[] selectedUserIds, string? messageBody)
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            var report = await _context.WeeklyReports.FirstOrDefaultAsync(x => x.ReportId == reportId);
            if (report == null) return NotFound();

            var canForward = report.CreatedByUserId == identity.User.UserId
                || await _context.MailboxMessages
                    .AsNoTracking()
                    .Where(x => x.ReportId == reportId)
                    .SelectMany(x => x.Recipients)
                    .AnyAsync(x => x.RecipientUserId == identity.User.UserId)
                || IsAdmin();

            if (!canForward) return Forbid();

            if (selectedUserIds == null || selectedUserIds.Length == 0)
            {
                TempData["Error"] = "กรุณาเลือกผู้รับรายงาน";
                return RedirectToAction(nameof(Details), new { id = reportId });
            }

            report.Status = "SENT_TO_BDM";
            report.SentToBdmAt = DateTime.Now;
            report.UpdatedAt = DateTime.Now;

            await SendReportMessageAsync(
                report,
                selectedUserIds,
                subjectPrefix: "ส่งต่อรายงานสรุปที่ตรวจแล้ว",
                body: messageBody,
                nextStatus: "SENT_TO_BDM");

            TempData["Success"] = "ส่งต่อรายงานเรียบร้อย";
            return RedirectToAction(nameof(Details), new { id = reportId });
        }

        public async Task<IActionResult> DownloadAttachment(int id)
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            var attachment = await _context.WeeklyReportAttachments
                .AsNoTracking()
                .Include(x => x.Report)
                .FirstOrDefaultAsync(x => x.AttachmentId == id);

            if (attachment == null) return NotFound();

            var canView = attachment.Report?.CreatedByUserId == identity.User.UserId
                || await _context.MailboxMessages
                    .AsNoTracking()
                    .Where(x => x.ReportId == attachment.ReportId)
                    .SelectMany(x => x.Recipients)
                    .AnyAsync(x => x.RecipientUserId == identity.User.UserId)
                || IsAdmin();

            if (!canView) return Forbid();

            var fullPath = Path.Combine(_env.WebRootPath, attachment.FilePath.TrimStart('/'));
            if (!System.IO.File.Exists(fullPath))
            {
                TempData["Error"] = "ไม่พบไฟล์แนบ";
                return RedirectToAction(nameof(Details), new { id = attachment.ReportId });
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, attachment.ContentType ?? "application/octet-stream", attachment.FileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return RedirectToAction("Login", "Auth");

            var attachment = await _context.WeeklyReportAttachments
                .Include(x => x.Report)
                .FirstOrDefaultAsync(x => x.AttachmentId == id);

            if (attachment == null) return RedirectToAction(nameof(Index));
            if (attachment.Report == null || !CanEditReport(attachment.Report, identity.User.UserId)) return Forbid();

            var fullPath = Path.Combine(_env.WebRootPath, attachment.FilePath.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);

            var reportId = attachment.ReportId;
            _context.WeeklyReportAttachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = reportId });
        }

        private async Task SendReportMessageAsync(
            WeeklyReport report,
            int[] selectedUserIds,
            string subjectPrefix,
            string? body,
            string nextStatus)
        {
            var identity = await GetCurrentIdentityAsync();
            if (identity.User == null) return;

            var recipientIds = selectedUserIds
                .Where(x => x > 0 && x != identity.User.UserId)
                .Distinct()
                .ToList();

            if (recipientIds.Count == 0) return;

            var recipients = await _context.LoginUsers
                .AsNoTracking()
                .Where(x => recipientIds.Contains(x.UserId) && x.Status == "ACTIVE")
                .ToListAsync();

            if (recipients.Count == 0) return;

            var message = new MailboxMessage
            {
                ReportId = report.ReportId,
                Subject = $"{subjectPrefix}: {report.Subject}",
                Body = body,
                MessageType = "WEEKLY_REPORT",
                SenderUserId = identity.User.UserId,
                SenderEmpId = identity.Employee?.EmpId,
                CreatedAt = DateTime.Now
            };

            foreach (var recipient in recipients)
            {
                var empId = await ResolveUserEmpIdAsync(recipient);
                message.Recipients.Add(new MailboxRecipient
                {
                    RecipientUserId = recipient.UserId,
                    RecipientEmpId = empId,
                    IsRead = false,
                    IsDeleted = false,
                    CreatedAt = DateTime.Now
                });
            }

            report.Status = nextStatus;
            report.UpdatedAt = DateTime.Now;
            _context.MailboxMessages.Add(message);
            await _context.SaveChangesAsync();
        }

        private async Task SaveAttachmentsAsync(int reportId, List<IFormFile>? files, int userId)
        {
            if (files == null || files.Count == 0) return;

            var folder = Path.Combine(_env.WebRootPath, "uploads", "weekly-reports", reportId.ToString());
            Directory.CreateDirectory(folder);

            foreach (var file in files.Where(x => x != null && x.Length > 0))
            {
                if (file.Length > MaxUploadSize)
                    continue;

                var extension = Path.GetExtension(file.FileName);
                var storedName = $"{Guid.NewGuid():N}{extension}";
                var fullPath = Path.Combine(folder, storedName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _context.WeeklyReportAttachments.Add(new WeeklyReportAttachment
                {
                    ReportId = reportId,
                    FileName = Path.GetFileName(file.FileName),
                    FilePath = $"/uploads/weekly-reports/{reportId}/{storedName}",
                    ContentType = file.ContentType,
                    FileSize = file.Length,
                    UploadedByUserId = userId,
                    UploadedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
        }

        private async Task<List<PendingWorkItemViewModel>> LoadPendingWorkAsync(int? empId)
        {
            if (!empId.HasValue) return new List<PendingWorkItemViewModel>();

            var projects = await _context.Projects
                .AsNoTracking()
                .Where(x => x.BaEmpId == empId.Value)
                .Select(x => new { x.ProjectId, x.ProjectName })
                .ToListAsync();

            var projectIds = projects.Select(x => x.ProjectId).ToHashSet();
            if (projectIds.Count == 0) return new List<PendingWorkItemViewModel>();

            var items = new List<PendingWorkItemViewModel>();

            var assigns = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(x => x.Employee)
                .Include(x => x.Phase!)
                    .ThenInclude(x => x.Project)
                .Where(x => x.Phase != null && projectIds.Contains(x.Phase.ProjectId))
                .ToListAsync();

            items.AddRange(assigns
                .Where(x => !IsDone(x.WorkStatus) && !IsClosedPhase(x.Phase?.PhaseStatus))
                .Select(x => new PendingWorkItemViewModel
                {
                    Type = "Assigns",
                    SourceId = x.AssignId,
                    ProjectName = x.Phase?.Project?.ProjectName ?? "-",
                    Title = string.IsNullOrWhiteSpace(x.Role) ? x.Phase?.PhaseName ?? "-" : x.Role!,
                    OwnerName = x.Employee?.EmpName,
                    Status = x.WorkStatus,
                    DueDate = x.PlanEnd,
                    TargetUrl = $"/PhaseAssigns?projectId={x.Phase?.ProjectId}&empId={x.EmpId}"
                }));

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Include(x => x.Project)
                .Include(x => x.Employee)
                .Where(x => projectIds.Contains(x.ProjectId))
                .ToListAsync();

            items.AddRange(issues
                .Where(x => !IsIssueDone(x.IssueStatus, x.DevStatus))
                .Select(x => new PendingWorkItemViewModel
                {
                    Type = "Issue",
                    SourceId = x.IssueId,
                    ProjectName = x.Project?.ProjectName ?? "-",
                    Title = x.IssueName,
                    OwnerName = x.Employee?.EmpName,
                    Status = x.IssueStatus,
                    DueDate = x.EndDate,
                    TargetUrl = $"/ProjectIssues/Details/{x.IssueId}"
                }));

            var supports = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(x => x.Project)
                .Include(x => x.Employee)
                .Where(x => projectIds.Contains(x.ProjectId))
                .ToListAsync();

            items.AddRange(supports
                .Where(x => !IsDone(x.Status))
                .Select(x => new PendingWorkItemViewModel
                {
                    Type = "Support",
                    SourceId = x.OrderId,
                    ProjectName = x.Project?.ProjectName ?? "-",
                    Title = string.IsNullOrWhiteSpace(x.OrderTitle) ? $"Support #{x.OrderId}" : x.OrderTitle!,
                    OwnerName = x.Employee?.EmpName,
                    Status = x.Status,
                    DueDate = x.EndDate,
                    TargetUrl = $"/SupportOrders/Details/{x.OrderId}"
                }));

            var followups = await _context.ProjectFollowups
                .AsNoTracking()
                .Include(x => x.Project)
                .Include(x => x.Owner)
                .Where(x => x.ProjectId.HasValue && projectIds.Contains(x.ProjectId.Value))
                .ToListAsync();

            items.AddRange(followups
                .Where(x => !IsFollowupDone(x.Status))
                .Select(x => new PendingWorkItemViewModel
                {
                    Type = "Followup",
                    SourceId = x.FollowupId,
                    ProjectName = x.Project?.ProjectName ?? "-",
                    Title = x.TaskTitle,
                    OwnerName = x.Owner?.EmpName,
                    Status = x.Status,
                    DueDate = x.NextFollowupDate,
                    TargetUrl = $"/Followups/Details/{x.FollowupId}"
                }));

            return items
                .OrderBy(x => x.DueDate.HasValue ? 0 : 1)
                .ThenBy(x => x.DueDate)
                .ThenBy(x => x.ProjectName)
                .Take(150)
                .ToList();
        }

        private async Task<List<UserOptionViewModel>> LoadUserOptionsAsync(int? excludeUserId = null)
        {
            var users = await _context.LoginUsers
                .AsNoTracking()
                .Where(x => x.Status == "ACTIVE")
                .ToListAsync();

            var empIds = users.Where(x => x.EmpId.HasValue).Select(x => x.EmpId!.Value).ToHashSet();
            var userIds = users.Select(x => x.UserId).ToHashSet();

            var employees = await _context.Employees
                .AsNoTracking()
                .Where(x => empIds.Contains(x.EmpId) || (x.LoginUserId.HasValue && userIds.Contains(x.LoginUserId.Value)))
                .ToListAsync();

            return users
                .Where(x => !excludeUserId.HasValue || x.UserId != excludeUserId.Value)
                .Select(user =>
                {
                    var employee = employees.FirstOrDefault(e => user.EmpId.HasValue && e.EmpId == user.EmpId.Value)
                        ?? employees.FirstOrDefault(e => e.LoginUserId == user.UserId);
                    return new UserOptionViewModel
                    {
                        UserId = user.UserId,
                        EmpId = employee?.EmpId,
                        Username = user.Username,
                        DisplayName = employee?.EmpName ?? user.Username,
                        Position = employee?.Position
                    };
                })
                .OrderBy(x => GetPositionOrder(x.Position))
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

        private async Task<(LoginUser? User, Employee? Employee)> GetCurrentIdentityAsync()
        {
            var userId = CurrentUserId();
            if (userId == null) return (null, null);

            var user = await _context.LoginUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId.Value);
            if (user == null) return (null, null);

            Employee? employee = null;
            if (user.EmpId.HasValue)
                employee = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmpId == user.EmpId.Value);

            employee ??= await _context.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.LoginUserId == user.UserId);

            return (user, employee);
        }

        private async Task<int?> ResolveUserEmpIdAsync(LoginUser user)
        {
            if (user.EmpId.HasValue) return user.EmpId.Value;

            return await _context.Employees
                .AsNoTracking()
                .Where(x => x.LoginUserId == user.UserId)
                .Select(x => (int?)x.EmpId)
                .FirstOrDefaultAsync();
        }

        private bool CanEditReport(WeeklyReport report, int userId)
            => report.CreatedByUserId == userId
                && string.Equals(report.Status, "DRAFT", StringComparison.OrdinalIgnoreCase);

        private bool ValidateReport(WeeklyReport report, string submitAction, int[] selectedUserIds)
        {
            if (string.IsNullOrWhiteSpace(report.Subject))
                ModelState.AddModelError("Report.Subject", "กรุณากรอกหัวข้อรายงาน");

            if (submitAction == "send" && (selectedUserIds == null || selectedUserIds.Length == 0))
                ModelState.AddModelError("SelectedUserIds", "กรุณาเลือกผู้รับรายงาน");

            if (report.WeekStart.HasValue && report.WeekEnd.HasValue && report.WeekEnd.Value.Date < report.WeekStart.Value.Date)
                ModelState.AddModelError("Report.WeekEnd", "วันที่สิ้นสุดสัปดาห์ต้องไม่น้อยกว่าวันเริ่มต้น");

            return ModelState.IsValid;
        }

        private static void NormalizeReportInput(WeeklyReport report)
        {
            report.Subject = (report.Subject ?? "").Trim();
            report.Summary = string.IsNullOrWhiteSpace(report.Summary) ? null : report.Summary.Trim();
        }

        private static (DateTime WeekStart, DateTime WeekEnd) GetDefaultWeekRange()
        {
            var today = DateTime.Today;
            var diff = ((int)today.DayOfWeek + 6) % 7;
            var start = today.AddDays(-diff);
            return (start, start.AddDays(6));
        }

        private int? CurrentUserId()
            => HttpContext.Session.GetInt32("UserId");

        private bool IsAdmin()
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            return string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPositionOrder(string? position)
        {
            var value = (position ?? "").Trim().ToUpperInvariant();
            if (value.Contains("BUSINESS DEVELOPMENT")) return 10;
            if (value.Contains("PROJECT MANAGER")) return 20;
            if (value.Contains("BUSINESS ANALYST")) return 30;
            return 90;
        }

        private static bool IsDone(string? status)
            => string.Equals((status ?? "").Trim(), "DONE", StringComparison.OrdinalIgnoreCase);

        private static bool IsFollowupDone(string? status)
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            return value is "DONE" or "ACK";
        }

        private static bool IsIssueDone(string? issueStatus, string? devStatus)
        {
            var issue = (issueStatus ?? "").Trim().ToUpperInvariant();
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();
            return issue is "FIXED" or "PASS" || dev == "FIXED";
        }

        private static bool IsClosedPhase(string? phaseStatus)
            => string.Equals((phaseStatus ?? "").Trim(), "ส่งงวดงานแล้ว", StringComparison.OrdinalIgnoreCase)
                || string.Equals((phaseStatus ?? "").Trim(), "อนุมัติจ่ายเงินแล้ว", StringComparison.OrdinalIgnoreCase);
    }
}
