using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    public class ProjectIssuesController : BaseController
    {
        private static readonly (string Value, string Text)[] TesterIssueStatuses =
        {
            ("OPEN", "OPEN - เปิดปัญหา / รอแก้"),
            ("FAIL", "FAIL - ทดสอบไม่ผ่าน / ส่งกลับแก้"),
            ("PASS", "PASS - ทดสอบผ่าน / ปิดงาน"),
            ("REJECT", "REJECT - ปฏิเสธ / ไม่ใช่ปัญหา")
        };

        private static readonly (string Value, string Text)[] ProgrammerDevStatuses =
        {
            ("WIP", "WIP - กำลังแก้"),
            ("FIXED", "FIXED - แก้เสร็จ / ส่งตรวจ")
        };

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly OverdueNotificationService _notificationService;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly ILogger<ProjectIssuesController> _logger;
        private const string FilterProjectIdKey = "ProjectIssues.Filter.ProjectId";
        private const string FilterEmpNameKey = "ProjectIssues.Filter.EmpName";
        private const string FilterStatusKey = "ProjectIssues.Filter.Status";
        private const string DevFilterProjectIdKey = "ProjectIssuesDev.Filter.ProjectId";
        private const string DevFilterEmpNameKey = "ProjectIssuesDev.Filter.EmpName";
        private const string DevFilterStatusKey = "ProjectIssuesDev.Filter.Status";
        private static readonly CultureInfo ThaiCulture = new("th-TH");

        public ProjectIssuesController(
            AppDbContext context,
            IWebHostEnvironment env,
            OverdueNotificationService notificationService,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            ILogger<ProjectIssuesController> logger)
        {
            _context = context;
            _env = env;
            _notificationService = notificationService;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _logger = logger;
        }

        // =====================================================
        // INDEX
        // =====================================================
        [RequireMenu("ProjectIssues.Index")]
        public async Task<IActionResult> Index(int? projectId, string? empName, string? status)
        {
            (projectId, empName) = ResolveIndexFilters(projectId, empName, FilterProjectIdKey, FilterEmpNameKey);
            var selectedStatus = ResolveStatusFilter(status, FilterStatusKey, NormalizeIndexIssueStatus);

            await LoadDropdown(projectId, empName);
            ViewBag.StatusList = BuildStatusFilterList(TesterIssueStatuses, selectedStatus);
            ViewBag.SelectedStatus = selectedStatus;

            if (!projectId.HasValue)
                return View(new List<ProjectIssue>());

            var issues = await GetIssues(projectId.Value, empName, issueStatus: selectedStatus);
            return View(issues);
        }

        // =====================================================
        // DEV INDEX (Programmer page)
        // =====================================================
        [RequireMenu("ProjectIssues.DevIndex")]
        public async Task<IActionResult> DevIndex(int? projectId, string? empName, string? status)
        {
            (projectId, empName) = ResolveIndexFilters(projectId, empName, DevFilterProjectIdKey, DevFilterEmpNameKey);
            var selectedStatus = ResolveStatusFilter(status, DevFilterStatusKey, NormalizeIndexDevStatus);

            await LoadDropdown(projectId, empName);
            ViewBag.StatusList = BuildStatusFilterList(ProgrammerDevStatuses, selectedStatus);
            ViewBag.SelectedStatus = selectedStatus;

            if (!projectId.HasValue)
                return View(new List<ProjectIssue>());

            var issues = await GetIssues(projectId.Value, empName, devStatus: selectedStatus);
            return View(issues);
        }

        // =====================================================
        // DETAILS (VIEW)
        // =====================================================
        [RequireMenu("ProjectIssues.View")]
        public async Task<IActionResult> Details(int id)
        {
            var issue = await _context.ProjectIssues
                .AsNoTracking()
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.Employee)
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null)
                return NotFound();

            ViewBag.GitHistories = await _context.ProjectIssueGitHistories
                .AsNoTracking()
                .Where(x => x.IssueId == issue.IssueId)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();

            return View(issue);
        }

        // =====================================================
        // DEV DETAILS (VIEW FOR PROGRAMMER PAGE)
        // =====================================================
        [RequireMenu("ProjectIssues.DevIndex")]
        public async Task<IActionResult> DevDetails(int id)
        {
            var issue = await _context.ProjectIssues
                .AsNoTracking()
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.Employee)
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null)
                return NotFound();

            ViewBag.GitHistories = await _context.ProjectIssueGitHistories
                .AsNoTracking()
                .Where(x => x.IssueId == issue.IssueId)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();

            return View(issue);
        }

        // =====================================================
        // VIEW ONLY REPORT
        // =====================================================
        [RequireMenu("ProjectIssues.ViewOnly")]
        public async Task<IActionResult> ViewOnly(int? projectId, int? baEmpId, string? empName, string? status, string? devStatus, int? departmentId)
        {
            departmentId = await ReportDepartmentSupport.LoadAsync(this, _context, departmentId);
            await LoadDropdown(projectId, empName, baEmpId, departmentId);
            status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
            devStatus = string.IsNullOrWhiteSpace(devStatus) ? null : devStatus.Trim().ToUpperInvariant();

            var query = _context.ProjectIssues
                .AsNoTracking()
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.Project)
                    .ThenInclude(p => p!.BA)
                .Include(i => i.Project)
                    .ThenInclude(p => p!.TeamMembers)
                        .ThenInclude(m => m.Employee)
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .Include(i => i.Employee)
                .AsQueryable();

            if (projectId.HasValue)
                query = query.Where(i => i.ProjectId == projectId.Value);
            if (departmentId.HasValue)
                query = query.Where(i => i.Project != null && i.Project.DepartmentId == departmentId.Value);

            if (baEmpId.HasValue)
                query = query.Where(i => i.Project != null
                    && (i.Project.BaEmpId == baEmpId.Value
                        || i.Project.TeamMembers.Any(m =>
                            m.MemberRole == ProjectTeamRoles.BusinessAnalyst
                            && m.EmpId == baEmpId.Value)));

            if (!string.IsNullOrWhiteSpace(empName))
                query = query.Where(i => i.Employee != null && i.Employee.EmpName == empName);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.IssueStatus == status);

            if (!string.IsNullOrWhiteSpace(devStatus))
                query = query.Where(i => i.DevStatus == devStatus);

            var issues = await query
                .OrderBy(i => i.Project != null && i.Project.Coop != null ? i.Project.Coop.CoopName : "")
                .ThenBy(i => i.Project != null ? i.Project.ProjectName : "")
                .ThenBy(i => i.Project != null && i.Project.BA != null ? i.Project.BA.EmpName : "")
                .ThenByDescending(i => i.IsReopen)
                .ThenByDescending(i => i.ReopenCount)
                .ThenBy(i => i.IssueId)
                .ToListAsync();

            ViewBag.StatusList = new[] { "OPEN", "PASS", "FAIL", "REJECT" };
            ViewBag.SelectedStatus = status ?? "";
            ViewBag.DevStatusList = new[] { "WIP", "FIXED" };
            ViewBag.SelectedDevStatus = devStatus ?? "";

            return View(issues);
        }

        // =====================================================
        // CREATE (GET)
        // =====================================================
        [RequireMenu("ProjectIssues.Create")]
        public async Task<IActionResult> Create(int projectId)
        {
            var model = new ProjectIssue
            {
                ProjectId = projectId,
                IssueStatus = "OPEN",
                IssuePriority = "NORMAL",
                CreatedAt = DateTime.Now
            };

            ViewBag.ProjectId = projectId;
            ViewBag.ProjectName = await GetProjectDisplayNameAsync(projectId);
            ViewBag.Employees = GetEmployeeList();
            ViewBag.StatusList = GetStatusList("OPEN");

            return View(model);
        }

        // =====================================================
        // CREATE (POST)
        // ✅ เพิ่ม: INSERT ProjectIssueStatusHistories (Initial status)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectIssues.Create")]
        public async Task<IActionResult> Create(ProjectIssue model, List<IFormFile>? images)
        {
            ApplyIssueDateInput(model);
            ValidateIssueDateRange(model, requireDates: true);

            if (!ModelState.IsValid)
            {
                ViewBag.ProjectId = model.ProjectId;
                ViewBag.ProjectName = await GetProjectDisplayNameAsync(model.ProjectId);
                ViewBag.Employees = GetEmployeeList(model.AssignTo);
                ViewBag.StatusList = GetStatusList(model.IssueStatus);
                return View(model);
            }

            // ✅ normalize (กัน null/ช่องว่าง)
            model.IssueStatus = NormalizeTesterIssueStatus(model.IssueStatus, "OPEN");
            model.IssuePriority = (model.IssuePriority ?? "NORMAL").Trim().ToUpperInvariant();
            model.DevStatus = "WIP";

            // ✅ CreatedAt: ถ้าหน้า Create ส่งมาเองก็ใช้ได้ ถ้าไม่ได้ส่งให้ใช้ตอนนี้
            if (model.CreatedAt == default)
                model.CreatedAt = DateTime.Now;

            // ✅ ค่าเริ่มต้น
            model.IsReopen = false;
            model.ReopenCount = 0;
            var projectBaEmpId = await _context.Projects
                .AsNoTracking()
                .Where(p => p.ProjectId == model.ProjectId)
                .Select(p => p.BaEmpId)
                .FirstOrDefaultAsync();
            model.CreatedBy = projectBaEmpId ?? await GetCurrentEntryIdAsync();

            _context.ProjectIssues.Add(model);
            await _context.SaveChangesAsync(); // ✅ ได้ IssueId แล้ว

            // =================================================
            // ✅ INSERT HISTORY (Initial Snapshot)
            // OldStatus = null, NewStatus = status เริ่มต้น
            // =================================================
            _context.ProjectIssueStatusHistories.Add(new ProjectIssueStatusHistory
            {
                IssueId = model.IssueId,
                OldStatus = null,
                NewStatus = model.IssueStatus,
                IsReopen = model.IsReopen,
                ReopenCount = model.ReopenCount,
                ChangedAt = model.CreatedAt,     // ให้ตรงกับตอนสร้าง
                ChangedByEmpId = model.CreatedBy ?? model.AssignTo
            });

            await _context.SaveChangesAsync();

            // =================================================
            // บันทึกรูปก่อนแก้
            // =================================================
            if (images != null && images.Count > 0)
            {
                string path = Path.Combine(_env.WebRootPath, "uploads", "issues", model.IssueId.ToString());
                Directory.CreateDirectory(path);

                foreach (var file in images)
                {
                    if (file.Length == 0) continue;
                    // ✅ จำกัดขนาดไฟล์ (กัน DoS / upload ใหญ่เกิน)
                    if (file.Length > 5 * 1024 * 1024) continue; // 5MB

                    // ✅ ตรวจ content-type แบบหยาบ
                    var contentType = (file.ContentType ?? "").ToLowerInvariant();
                    if (!contentType.StartsWith("image/")) continue;

                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp")
                        continue;

                    string fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    string filePath = Path.Combine(path, fileName);

                    using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);

                    _context.ProjectIssueImages.Add(new ProjectIssueImage
                    {
                        IssueId = model.IssueId,
                        FileName = fileName,
                        FilePath = $"/uploads/issues/{model.IssueId}/{fileName}",
                        UploadedAt = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
            }

            await SyncNotificationsSafelyAsync();
            await SendCreatedIssueTelegramSafelyAsync(model.IssueId);

            return RedirectToAction(nameof(Index), new { projectId = model.ProjectId });
        }

        // =====================================================
        // EDIT (GET)
        // =====================================================
        [RequireMenu("ProjectIssues.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var issue = await _context.ProjectIssues
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null) return NotFound();

            ViewBag.ProjectId = issue.ProjectId;
            ViewBag.ProjectName = issue.Project?.ProjectDisplayName;
            ViewBag.Employees = GetEmployeeList(issue.AssignTo);
            ViewBag.CurrentIssueStatus = issue.IssueStatus;
            ViewBag.CurrentDevStatus = issue.DevStatus;
            ViewBag.StatusList = GetStatusList(issue.IssueStatus);
            ViewBag.GitHistories = await _context.ProjectIssueGitHistories
                .AsNoTracking()
                .Where(x => x.IssueId == issue.IssueId)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();

            return View(issue);
        }

        // =====================================================
        // EDIT (POST)  🔁 REOPEN LOGIC + ✅ INSERT STATUS HISTORY
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectIssues.Edit")]
        public async Task<IActionResult> Edit(int id, ProjectIssue model, List<IFormFile>? newImages, List<int>? deleteImageIds)
        {
            if (id != model.IssueId) return NotFound();

            var issue = await _context.ProjectIssues
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null) return NotFound();

            ApplyIssueDateInput(model);
            ValidateIssueDateRange(model, requireDates: true);

            var oldStatus = (issue.IssueStatus ?? "").Trim().ToUpperInvariant();
            var newStatus = NormalizeTesterIssueStatus(model.IssueStatus, oldStatus);
            var currentDevStatus = NormalizeProgrammerDevStatus(issue.DevStatus);
            var statusChanged = !string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase);
            var shouldCountFailRound = newStatus == "FAIL" && currentDevStatus == "FIXED";
            var shouldNotifyAssigneeBaResult = IsBaResultStatus(newStatus);
            if (RequiresFixedDevStatusForBaResult(newStatus) && currentDevStatus != "FIXED")
            {
                ModelState.AddModelError(
                    nameof(ProjectIssue.IssueStatus),
                    "ไม่สามารถบันทึกสถานะ PASS หรือ FAIL ได้ เนื่องจาก Dev Status ยังไม่เป็น FIXED");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.ProjectId = model.ProjectId;
                ViewBag.ProjectName = await GetProjectDisplayNameAsync(model.ProjectId);
                ViewBag.Employees = GetEmployeeList(model.AssignTo);
                ViewBag.CurrentIssueStatus = issue.IssueStatus;
                ViewBag.CurrentDevStatus = issue.DevStatus;
                ViewBag.StatusList = GetStatusList(model.IssueStatus);
                ViewBag.GitHistories = await _context.ProjectIssueGitHistories
                    .AsNoTracking()
                    .Where(x => x.IssueId == issue.IssueId)
                    .OrderByDescending(x => x.EntryDate)
                    .ToListAsync();
                model.DevStatus = issue.DevStatus;
                model.Images = await _context.ProjectIssueImages
                    .AsNoTracking()
                    .Where(x => x.IssueId == model.IssueId)
                    .ToListAsync();
                return View(model);
            }

            issue.IssueName = model.IssueName;
            issue.IssueDetail = model.IssueDetail;   // BA detail
            issue.AssignTo = model.AssignTo;
            issue.IssueStatus = newStatus;
            issue.IssuePriority = (model.IssuePriority ?? issue.IssuePriority ?? "NORMAL").Trim().ToUpperInvariant();
            issue.StartDate = model.StartDate;
            issue.EndDate = model.EndDate;

            if (newStatus == "FAIL" || newStatus == "OPEN")
            {
                issue.DevStatus = "WIP";
                _context.Entry(issue).Property(x => x.DevStatus).IsModified = true;
            }

            if (shouldCountFailRound)
            {
                issue.IsReopen = true;
                issue.ReopenCount += 1;

                _context.Entry(issue).Property(x => x.IsReopen).IsModified = true;
                _context.Entry(issue).Property(x => x.ReopenCount).IsModified = true;
            }

            // Insert history when the status changes, or when BA fails another fixed round.
            if (statusChanged || shouldCountFailRound)
            {
                var changedByEmpId = await GetCurrentEntryIdAsync();

                _context.ProjectIssueStatusHistories.Add(new ProjectIssueStatusHistory
                {
                    IssueId = issue.IssueId,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    IsReopen = issue.IsReopen,
                    ReopenCount = issue.ReopenCount,
                    ChangedAt = DateTime.Now,
                    ChangedByEmpId = changedByEmpId ?? issue.AssignTo
                });
            }

            await _context.SaveChangesAsync();
            // ================= DELETE BEFORE IMAGES =================
            if (deleteImageIds != null && deleteImageIds.Count > 0)
            {
                var imagesToDelete = await _context.ProjectIssueImages
                    .Where(x => deleteImageIds.Contains(x.ImageId))
                    .ToListAsync();

                foreach (var img in imagesToDelete)
                {
                    var filePath = Path.Combine(_env.WebRootPath, img.FilePath.TrimStart('/'));

                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                }

                _context.ProjectIssueImages.RemoveRange(imagesToDelete);
                await _context.SaveChangesAsync();
            }

            // ================= UPLOAD NEW BEFORE IMAGES =================
            if (newImages != null && newImages.Count > 0)
            {
                string path = Path.Combine(_env.WebRootPath, "uploads", "issues", issue.IssueId.ToString());
                Directory.CreateDirectory(path);

                foreach (var file in newImages)
                {
                    if (file.Length == 0) continue;

                    string fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    string fullPath = Path.Combine(path, fileName);

                    using var stream = new FileStream(fullPath, FileMode.Create);
                    await file.CopyToAsync(stream);

                    _context.ProjectIssueImages.Add(new ProjectIssueImage
                    {
                        IssueId = issue.IssueId,
                        FileName = fileName,
                        FilePath = $"/uploads/issues/{issue.IssueId}/{fileName}",
                        UploadedAt = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
            }

            if (newStatus != "OPEN")
            {
                await SyncNotificationsSafelyAsync();
                if (shouldNotifyAssigneeBaResult)
                    await SendBaResultIssueTelegramToAssigneeSafelyAsync(issue.IssueId);
            }

            return RedirectToAction(nameof(Index), new { projectId = issue.ProjectId });
        }

        // =====================================================
        // DEV EDIT (GET) - separate screen for programmers
        // =====================================================
        [RequireMenu("ProjectIssues.DevIndex")]
        public async Task<IActionResult> DevEdit(int id)
        {
            var issue = await _context.ProjectIssues
                .Include(i => i.Employee)
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null) return NotFound();

            ViewBag.CurrentDevStatus = issue.DevStatus;
            ViewBag.DevStatusList = GetDevStatusList(issue.DevStatus);
            ViewBag.GitHistories = await _context.ProjectIssueGitHistories
                .AsNoTracking()
                .Where(x => x.IssueId == issue.IssueId)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();
            return View(issue);
        }

        // =====================================================
        // DEV EDIT (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectIssues.DevIndex")]
        public async Task<IActionResult> DevEdit(
            int id,
            ProjectIssue model,
            List<IFormFile>? afterImages,
            List<int>? deleteFixImageIds,
            List<string>? gitTypes,
            List<string>? gitIds)
        {
            if (id != model.IssueId) return NotFound();

            var issue = await _context.ProjectIssues
                .Include(i => i.Employee)
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);
            if (issue == null) return NotFound();

            ModelState.Remove(nameof(ProjectIssue.Project));
            ModelState.Remove(nameof(ProjectIssue.Employee));
            ModelState.Remove(nameof(ProjectIssue.Images));
            ModelState.Remove(nameof(ProjectIssue.FixImages));
            ModelState.Remove(nameof(ProjectIssue.IssueName));
            ModelState.Remove(nameof(ProjectIssue.IssueDetail));
            ModelState.Remove(nameof(ProjectIssue.IssueStatus));
            ModelState.Remove(nameof(ProjectIssue.IssuePriority));
            ModelState.Remove(nameof(ProjectIssue.AssignTo));
            ModelState.Remove(nameof(ProjectIssue.StartDate));
            ModelState.Remove(nameof(ProjectIssue.EndDate));
            ModelState.Remove(nameof(ProjectIssue.CreatedAt));
            ModelState.Remove(nameof(ProjectIssue.CreatedBy));
            ModelState.Remove(nameof(ProjectIssue.UpdatedAt));

            var currentDbDevStatus = NormalizeProgrammerDevStatus(issue.DevStatus);
            var canAddGitHistory = currentDbDevStatus == "WIP";
            var newDev = NormalizeProgrammerDevStatus(model.DevStatus);
            var gitHistoryRows = canAddGitHistory
                ? BuildGitHistoryRows(gitTypes, gitIds, ModelState)
                : new List<(string GitType, string GitId)>();

            if (!ModelState.IsValid)
            {
                ViewBag.CurrentDevStatus = issue.DevStatus;
                ViewBag.DevStatusList = GetDevStatusList(model.DevStatus);
                ViewBag.GitHistories = await _context.ProjectIssueGitHistories
                    .AsNoTracking()
                    .Where(x => x.IssueId == issue.IssueId)
                    .OrderByDescending(x => x.EntryDate)
                    .ToListAsync();
                issue.DevStatus = model.DevStatus;
                issue.DevDetail = model.DevDetail;
                return View(issue);
            }

            var shouldNotifyBaFixed = newDev == "FIXED";

            issue.DevStatus = newDev;
            issue.DevDetail = model.DevDetail;   // developer fix detail

            _context.Entry(issue).Property(x => x.DevStatus).IsModified = true;
            _context.Entry(issue).Property(x => x.DevDetail).IsModified = true;

            if (gitHistoryRows.Count > 0)
            {
                var entryDate = DateTime.Now;
                var currentEmpId = await GetCurrentEntryIdAsync();

                foreach (var row in gitHistoryRows)
                {
                    _context.ProjectIssueGitHistories.Add(new ProjectIssueGitHistory
                    {
                        IssueId = issue.IssueId,
                        GitType = row.GitType,
                        GitId = row.GitId,
                        EntryDate = entryDate,
                        CreatedByEmpId = currentEmpId
                    });
                }
            }

            await _context.SaveChangesAsync();

            // ================= DELETE AFTER FIX IMAGES =================
            if (deleteFixImageIds != null && deleteFixImageIds.Count > 0)
            {
                var imagesToDelete = await _context.ProjectIssueFixImages
                    .Where(x => deleteFixImageIds.Contains(x.ImageId))
                    .ToListAsync();

                foreach (var img in imagesToDelete)
                {
                    var filePath = Path.Combine(_env.WebRootPath, img.FilePath.TrimStart('/'));

                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                }

                _context.ProjectIssueFixImages.RemoveRange(imagesToDelete);
                await _context.SaveChangesAsync();
            }

            // ✅ save After images to FixImages
            await SaveFixImages(issue.IssueId, afterImages);

            await SyncNotificationsSafelyAsync();
            if (shouldNotifyBaFixed)
                await SendFixedIssueTelegramToBaSafelyAsync(issue.IssueId);

            return RedirectToAction(nameof(DevIndex), new { projectId = issue.ProjectId });
        }

        // =====================================================
        // DELETE
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectIssues.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var issue = await _context.ProjectIssues
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null)
                return NotFound();

            var projectId = issue.ProjectId;

            // ✅ remove status history rows
            var histories = await _context.ProjectIssueStatusHistories
                .Where(h => h.IssueId == id)
                .ToListAsync();
            if (histories.Any())
                _context.ProjectIssueStatusHistories.RemoveRange(histories);

            if (issue.Images != null && issue.Images.Any())
                _context.ProjectIssueImages.RemoveRange(issue.Images);

            if (issue.FixImages != null && issue.FixImages.Any())
                _context.ProjectIssueFixImages.RemoveRange(issue.FixImages);

            _context.ProjectIssues.Remove(issue);
            await _context.SaveChangesAsync();

            // ✅ remove physical image folders
            DeleteIssueFiles(id);

            await SyncNotificationsSafelyAsync();

            return RedirectToAction(nameof(Index), new { projectId = projectId });
        }

        // =====================================================
        // QUERY
        // =====================================================
        private async Task<List<ProjectIssue>> GetIssues(
            int projectId,
            string? empName,
            string? issueStatus = null,
            string? devStatus = null)
        {
            var query = _context.ProjectIssues
                .AsNoTracking()
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .Include(i => i.Employee)
                    .ThenInclude(e => e!.LoginUser)
                .Where(i => i.ProjectId == projectId);

            if (!string.IsNullOrEmpty(empName))
                query = query.Where(i => i.Employee != null && i.Employee.EmpName == empName);

            if (!string.IsNullOrWhiteSpace(issueStatus))
                query = query.Where(i => i.IssueStatus == issueStatus);

            if (!string.IsNullOrWhiteSpace(devStatus))
                query = query.Where(i => i.DevStatus == devStatus);

            return await query
                .OrderByDescending(i => i.IsReopen)
                .ThenByDescending(i => i.IssuePriority == "URGENT")
                .ThenBy(i => i.IssueId)
                .ToListAsync();
        }

        private (int? ProjectId, string? EmpName) ResolveIndexFilters(
            int? projectId,
            string? empName,
            string projectKey,
            string empNameKey)
        {
            var hasProjectQuery = Request.Query.ContainsKey("projectId");
            var hasEmpQuery = Request.Query.ContainsKey("empName");
            var storedProjectId = HttpContext.Session.GetInt32(projectKey);
            var projectChangedByQuery = false;

            if (!hasProjectQuery)
            {
                projectId = storedProjectId;
            }
            else if (projectId.HasValue && projectId.Value > 0)
            {
                projectChangedByQuery = storedProjectId.HasValue && storedProjectId.Value != projectId.Value;
                HttpContext.Session.SetInt32(projectKey, projectId.Value);
            }
            else
            {
                HttpContext.Session.Remove(projectKey);
                HttpContext.Session.Remove(empNameKey);
                empName = null;
            }

            if (!projectId.HasValue || projectId.Value <= 0)
            {
                HttpContext.Session.Remove(empNameKey);
                return (projectId, null);
            }

            if (!hasEmpQuery)
            {
                if (projectChangedByQuery)
                {
                    HttpContext.Session.Remove(empNameKey);
                    empName = null;
                }
                else
                {
                    empName = HttpContext.Session.GetString(empNameKey);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(empName))
                {
                    empName = empName.Trim();
                    HttpContext.Session.SetString(empNameKey, empName);
                }
                else
                {
                    HttpContext.Session.Remove(empNameKey);
                    empName = null;
                }
            }

            return (projectId, empName);
        }

        private static string NormalizeIndexIssueStatus(string? status)
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            return TesterIssueStatuses.Any(x => x.Value == value) ? value : "";
        }

        private static string NormalizeIndexDevStatus(string? status)
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            return ProgrammerDevStatuses.Any(x => x.Value == value) ? value : "";
        }

        private string ResolveStatusFilter(
            string? status,
            string statusKey,
            Func<string?, string> normalize)
        {
            if (!Request.Query.ContainsKey("status"))
            {
                return normalize(HttpContext.Session.GetString(statusKey));
            }

            var selectedStatus = normalize(status);
            if (string.IsNullOrWhiteSpace(selectedStatus))
            {
                HttpContext.Session.Remove(statusKey);
                return "";
            }

            HttpContext.Session.SetString(statusKey, selectedStatus);
            return selectedStatus;
        }

        private static SelectList BuildStatusFilterList(
            IEnumerable<(string Value, string Text)> statuses,
            string? selected)
        {
            return new SelectList(
                statuses.Select(x => new { x.Value, x.Text }),
                "Value",
                "Text",
                selected
            );
        }

        private static List<(string GitType, string GitId)> BuildGitHistoryRows(
            List<string>? gitTypes,
            List<string>? gitIds,
            Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
        {
            var rows = new List<(string GitType, string GitId)>();
            var count = Math.Max(gitTypes?.Count ?? 0, gitIds?.Count ?? 0);

            for (var i = 0; i < count; i++)
            {
                var gitId = i < (gitIds?.Count ?? 0) ? (gitIds![i] ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(gitId))
                    continue;

                if (gitId.Length > 80)
                {
                    modelState.AddModelError("gitIds", "Git ID ต้องไม่เกิน 80 ตัวอักษร");
                    continue;
                }

                var gitType = NormalizeGitType(i < (gitTypes?.Count ?? 0) ? gitTypes![i] : null);
                if (gitType == null)
                {
                    modelState.AddModelError("gitTypes", "ประเภท Git ต้องเป็น GITHUB หรือ GITLAB เท่านั้น");
                    continue;
                }

                rows.Add((gitType, gitId));
            }

            return rows;
        }

        private static string? NormalizeGitType(string? gitType)
        {
            var value = (gitType ?? "").Trim().ToUpperInvariant();
            return value is "GITHUB" or "GITLAB" ? value : null;
        }

        // =====================================================
        // SAVE FIX IMAGES
        // =====================================================
        private async Task SaveFixImages(int issueId, List<IFormFile>? images)
        {
            if (images == null || images.Count == 0) return;

            string path = Path.Combine(_env.WebRootPath, "uploads", "issues_fix", issueId.ToString());
            Directory.CreateDirectory(path);

            foreach (var file in images)
            {
                if (file.Length == 0) continue;
                if (file.Length > 5 * 1024 * 1024) continue; // 5MB

                var contentType = (file.ContentType ?? "").ToLowerInvariant();
                if (!contentType.StartsWith("image/")) continue;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp")
                    continue;

                string fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                string filePath = Path.Combine(path, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream);

                _context.ProjectIssueFixImages.Add(new ProjectIssueFixImage
                {
                    IssueId = issueId,
                    FileName = fileName,
                    FilePath = $"/uploads/issues_fix/{issueId}/{fileName}",
                    UploadedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
        }

        // =====================================================
        private void DeleteIssueFiles(int issueId)
        {
            try
            {
                var issueDir = Path.Combine(_env.WebRootPath, "uploads", "issues", issueId.ToString());
                if (Directory.Exists(issueDir))
                    Directory.Delete(issueDir, recursive: true);

                var fixDir = Path.Combine(_env.WebRootPath, "uploads", "issues_fix", issueId.ToString());
                if (Directory.Exists(fixDir))
                    Directory.Delete(fixDir, recursive: true);
            }
            catch
            {
                // ignore file system errors; DB delete should still succeed
            }
        }

        private async Task LoadDropdown(int? projectId, string? empName, int? baEmpId = null, int? departmentId = null)
        {
            ViewBag.Projects = await _context.Projects
                .Include(p => p.Coop)
                .Where(p => !departmentId.HasValue || p.DepartmentId == departmentId.Value)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.SelectedEmp = empName;
            ViewBag.SelectedBA = baEmpId;

            var baQuery = _context.Employees
                .AsNoTracking()
                .Where(employee => _context.Projects.Any(projectRow =>
                    (!departmentId.HasValue || projectRow.DepartmentId == departmentId.Value)
                    && _context.ProjectIssues.Any(issue => issue.ProjectId == projectRow.ProjectId)
                    && (projectRow.BaEmpId == employee.EmpId
                        || projectRow.TeamMembers.Any(member =>
                            member.MemberRole == ProjectTeamRoles.BusinessAnalyst
                            && member.EmpId == employee.EmpId))))
                .Select(employee => new
                {
                    employee.EmpId,
                    employee.EmpName
                });

            ViewBag.BAList = await baQuery
                .Distinct()
                .OrderBy(x => x.EmpName)
                .Select(x => new SelectListItem
                {
                    Value = x.EmpId.ToString(),
                    Text = x.EmpName
                })
                .ToListAsync();

            if (!projectId.HasValue)
            {
                ViewBag.SelectedProject = null;
                ViewBag.EmpList = await _context.ProjectIssues
                .Include(i => i.Employee)
                    .Where(i => (!departmentId.HasValue || (i.Project != null && i.Project.DepartmentId == departmentId.Value)) && i.Employee != null && i.Employee.EmpName != null && i.Employee.EmpName != "")
                    .Select(i => i.Employee!.EmpName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();
                return;
            }

            var project = await _context.Projects
                .Include(p => p.Coop)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId.Value);

            ViewBag.SelectedProject = project;

            if (project != null)
            {
                ViewBag.EmpList = await _context.ProjectIssues
                    .Include(i => i.Employee)
                    .Where(i => i.ProjectId == projectId.Value)
                    .Where(i => i.Employee != null && i.Employee.EmpName != null && i.Employee.EmpName != "")
                    .Select(i => i.Employee!.EmpName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();
            }
            else
            {
                ViewBag.EmpList = new List<string>();
            }
        }

        private async Task<string?> GetProjectDisplayNameAsync(int projectId)
        {
            var project = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            return project?.ProjectDisplayName;
        }

        private SelectList GetEmployeeList(int? selected = null)
        {
            return new SelectList(
                _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .Select(e => new { e.EmpId, e.EmpName })
                    .ToList(),
                "EmpId",
                "EmpName",
                selected
            );
        }

        private SelectList GetStatusList(string? selected = null, bool includeOpen = true)
        {
            var selectedValue = (selected ?? "").Trim().ToUpperInvariant();
            var statuses = TesterIssueStatuses.ToList();
            if (!includeOpen)
            {
                statuses.RemoveAll(x => x.Value == "OPEN");
            }

            return new SelectList(
                statuses.Select(x => new { x.Value, x.Text }),
                "Value",
                "Text",
                selectedValue
            );
        }

        private SelectList GetDevStatusList(string? selected = null)
        {
            var selectedValue = NormalizeProgrammerDevStatus(selected);
            return new SelectList(
                ProgrammerDevStatuses.Select(x => new { x.Value, x.Text }),
                "Value",
                "Text",
                selectedValue
            );
        }

        private static string NormalizeTesterIssueStatus(string? status, string? fallback = "OPEN")
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            if (TesterIssueStatuses.Any(x => x.Value == value))
                return value;

            var fallbackValue = (fallback ?? "OPEN").Trim().ToUpperInvariant();
            return TesterIssueStatuses.Any(x => x.Value == fallbackValue) ? fallbackValue : "OPEN";
        }

        private static string NormalizeProgrammerDevStatus(string? status)
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            if (value == "TODO" || value == "DOING" || value == "BLOCK")
                return "WIP";
            return ProgrammerDevStatuses.Any(x => x.Value == value) ? value : "WIP";
        }

        private static bool RequiresFixedDevStatusForBaResult(string? status)
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            return value == "PASS" || value == "FAIL";
        }

        private static bool IsBaResultStatus(string? status)
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            return value == "PASS" || value == "FAIL" || value == "REJECT";
        }

        private void ApplyIssueDateInput(ProjectIssue model)
        {
            ModelState.Remove(nameof(ProjectIssue.StartDate));
            ModelState.Remove(nameof(ProjectIssue.EndDate));

            var startRaw = Request.Form[nameof(ProjectIssue.StartDate)].ToString();
            var endRaw = Request.Form[nameof(ProjectIssue.EndDate)].ToString();

            model.StartDate = ParseIssueDate(startRaw);
            model.EndDate = ParseIssueDate(endRaw);

            if (!string.IsNullOrWhiteSpace(startRaw) && !model.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(ProjectIssue.StartDate), "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
            }

            if (!string.IsNullOrWhiteSpace(endRaw) && !model.EndDate.HasValue)
            {
                ModelState.AddModelError(nameof(ProjectIssue.EndDate), "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
            }
        }

        private static DateTime? ParseIssueDate(string? value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return null;

            var isoParts = value.Split('-');
            if (isoParts.Length == 3
                && int.TryParse(isoParts[0], out var isoYear)
                && int.TryParse(isoParts[1], out var isoMonth)
                && int.TryParse(isoParts[2], out var isoDay))
            {
                isoYear = NormalizeThaiCalendarYear(isoYear);

                try
                {
                    return new DateTime(isoYear, isoMonth, isoDay);
                }
                catch
                {
                    return null;
                }
            }

            var parts = value.Split('/');
            if (parts.Length == 3
                && int.TryParse(parts[0], out var day)
                && int.TryParse(parts[1], out var month)
                && int.TryParse(parts[2], out var year))
            {
                year = NormalizeThaiCalendarYear(year);

                try
                {
                    return new DateTime(year, month, day);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static int NormalizeThaiCalendarYear(int year)
        {
            while (year > 2200)
            {
                year -= 543;
            }

            return year;
        }

        private void ValidateIssueDateRange(ProjectIssue model, bool requireDates = false)
        {
            if (requireDates && !model.StartDate.HasValue && !HasModelError(nameof(ProjectIssue.StartDate)))
            {
                ModelState.AddModelError(nameof(ProjectIssue.StartDate), "กรุณากรอกวันที่เริ่ม");
            }

            if (requireDates && !model.EndDate.HasValue && !HasModelError(nameof(ProjectIssue.EndDate)))
            {
                ModelState.AddModelError(nameof(ProjectIssue.EndDate), "กรุณากรอกวันที่สิ้นสุด");
            }

            if (model.StartDate.HasValue
                && model.EndDate.HasValue
                && model.EndDate.Value.Date < model.StartDate.Value.Date)
            {
                ModelState.AddModelError(nameof(ProjectIssue.EndDate), "วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่ม");
            }
        }

        private bool HasModelError(string key)
        {
            return ModelState.TryGetValue(key, out var state) && state.Errors.Count > 0;
        }

        private async Task SendCreatedIssueTelegramSafelyAsync(int issueId)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.ProjectIssuesCreate, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.ProjectIssuesCreate, HttpContext.RequestAborted);

            if (!sendLine && !sendTelegram)
                return;

            try
            {
                var issue = await _context.ProjectIssues
                    .AsNoTracking()
                    .Include(x => x.Employee)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.Coop)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.BA)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.TeamMembers)
                            .ThenInclude(m => m.Employee)
                    .FirstOrDefaultAsync(x => x.IssueId == issueId);

                if (issue == null)
                    return;

                var project = issue.Project;
                var recipientTargets = new Dictionary<int, string>();

                foreach (var ba in project?.BusinessAnalysts ?? Array.Empty<Employee>())
                    recipientTargets[ba.EmpId] = $"/ProjectIssues/Details/{issue.IssueId}";

                if (issue.AssignTo > 0)
                    recipientTargets.TryAdd(issue.AssignTo, $"/ProjectIssues/DevDetails/{issue.IssueId}");

                if (recipientTargets.Count == 0)
                    return;

                var title = "แจ้ง Issue ใหม่:";
                var message = BuildCreatedIssueTelegramMessage(issue);

                foreach (var recipient in recipientTargets)
                {
                    await SendChatNotificationToEmployeeSafelyAsync(
                        recipient.Key,
                        title,
                        message,
                        recipient.Value,
                        sendLine,
                        sendTelegram,
                        "created issue",
                        issue.IssueId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send created issue Telegram notification failed. IssueId={IssueId}", issueId);
            }
        }

        private async Task SendFixedIssueTelegramToBaSafelyAsync(int issueId)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.ProjectIssuesFixed, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.ProjectIssuesFixed, HttpContext.RequestAborted);

            if (!sendLine && !sendTelegram)
                return;

            try
            {
                var issue = await _context.ProjectIssues
                    .AsNoTracking()
                    .Include(x => x.Employee)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.Coop)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.BA)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.TeamMembers)
                            .ThenInclude(m => m.Employee)
                    .FirstOrDefaultAsync(x => x.IssueId == issueId);

                var baEmpIds = issue?.Project?.BusinessAnalysts.Select(ba => ba.EmpId).Distinct().ToList()
                    ?? new List<int>();
                if (issue == null || baEmpIds.Count == 0)
                    return;

                var message = BuildFixedIssueTelegramMessage(issue);
                foreach (var baEmpId in baEmpIds)
                {
                    await SendChatNotificationToEmployeeSafelyAsync(
                        baEmpId,
                        "แจ้ง Issue แก้เสร็จ:",
                        message,
                        $"/ProjectIssues/Details/{issue.IssueId}",
                        sendLine,
                        sendTelegram,
                        "fixed issue",
                        issue.IssueId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send fixed issue Telegram notification failed. IssueId={IssueId}", issueId);
            }
        }

        private async Task SendBaResultIssueTelegramToAssigneeSafelyAsync(int issueId)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.ProjectIssuesBaResult, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.ProjectIssuesBaResult, HttpContext.RequestAborted);

            if (!sendLine && !sendTelegram)
                return;

            try
            {
                var issue = await _context.ProjectIssues
                    .AsNoTracking()
                    .Include(x => x.Employee)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.Coop)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.BA)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.TeamMembers)
                            .ThenInclude(m => m.Employee)
                    .FirstOrDefaultAsync(x => x.IssueId == issueId);

                if (issue == null || issue.AssignTo <= 0)
                    return;

                var title = $"แจ้งผลตรวจ Issue: {TextOrDash(issue.IssueStatus)}";
                var message = BuildBaResultIssueTelegramMessage(issue);
                var targetUrl = $"/ProjectIssues/DevDetails/{issue.IssueId}";

                await SendChatNotificationToEmployeeSafelyAsync(
                    issue.AssignTo,
                    title,
                    message,
                    targetUrl,
                    sendLine,
                    sendTelegram,
                    "BA result issue",
                    issue.IssueId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send BA result issue Telegram notification failed. IssueId={IssueId}", issueId);
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

        private static string BuildCreatedIssueTelegramMessage(ProjectIssue issue)
        {
            var project = issue.Project;
            var rows = new List<string>
            {
                $"สหกรณ์: {TextOrDash(project?.Coop?.CoopName)}",
                $"Project: {ProjectNameForTelegram(project)}",
                $"Issue: {TextOrDash(issue.IssueName)}",
                $"รายละเอียด: {TextOrDash(issue.IssueDetail)}",
                $"เจ้าของงาน: {TextOrDash(issue.Employee?.EmpName)}",
                $"BA: {TextOrDash(project?.BusinessAnalystNames)}",
                $"Priority: {TextOrDash(issue.IssuePriority)}",
                $"Status: {TextOrDash(issue.IssueStatus)} / Dev {TextOrDash(issue.DevStatus)}",
                $"วันที่เริ่ม: {DateText(issue.StartDate)}",
                $"วันที่สิ้นสุด: {DateText(issue.EndDate)}"
            };

            return string.Join("\n", rows);
        }

        private static string BuildFixedIssueTelegramMessage(ProjectIssue issue)
        {
            var project = issue.Project;
            var rows = new List<string>
            {
                $"สหกรณ์: {TextOrDash(project?.Coop?.CoopName)}",
                $"Project: {ProjectNameForTelegram(project)}",
                $"Issue: {TextOrDash(issue.IssueName)}",
                $"เจ้าของงาน: {TextOrDash(issue.Employee?.EmpName)}",
                $"BA: {TextOrDash(project?.BusinessAnalystNames)}",
                $"Dev Status: {TextOrDash(issue.DevStatus)}",
                $"รายละเอียดการแก้ไข: {TextOrDash(issue.DevDetail)}",
                $"วันที่เริ่ม: {DateText(issue.StartDate)}",
                $"วันที่สิ้นสุด: {DateText(issue.EndDate)}"
            };

            return string.Join("\n", rows);
        }

        private static string BuildBaResultIssueTelegramMessage(ProjectIssue issue)
        {
            var project = issue.Project;
            var rows = new List<string>
            {
                $"สหกรณ์: {TextOrDash(project?.Coop?.CoopName)}",
                $"Project: {ProjectNameForTelegram(project)}",
                $"Issue: {TextOrDash(issue.IssueName)}",
                $"เจ้าของงาน: {TextOrDash(issue.Employee?.EmpName)}",
                $"BA: {TextOrDash(project?.BusinessAnalystNames)}",
                $"Status: {TextOrDash(issue.IssueStatus)} / Dev {TextOrDash(issue.DevStatus)}",
                $"รายละเอียดปัญหา: {TextOrDash(issue.IssueDetail)}",
                $"วันที่เริ่ม: {DateText(issue.StartDate)}",
                $"วันที่สิ้นสุด: {DateText(issue.EndDate)}"
            };

            return string.Join("\n", rows);
        }

        private static string ProjectNameForTelegram(Project? project)
            => string.IsNullOrWhiteSpace(project?.ProjectName) ? "-" : project.ProjectName.Trim();

        private static string TextOrDash(string? value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

        private static string DateText(DateTime? value)
            => value.HasValue ? value.Value.ToString("dd MMM yyyy", ThaiCulture) : "-";

        private async Task SyncNotificationsSafelyAsync()
        {
            try
            {
                await _notificationService.SyncAsync(HttpContext.RequestAborted);
            }
            catch
            {
                // Notification sync should not block the main save flow.
            }
        }

        private async Task<int?> GetCurrentEntryIdAsync()
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

    }
}
