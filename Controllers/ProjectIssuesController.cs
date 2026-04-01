using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class ProjectIssuesController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProjectIssuesController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // =====================================================
        // INDEX
        // =====================================================
        [RequireMenu("ProjectIssues.Index")]
        public async Task<IActionResult> Index(int? projectId, string? empName)
        {
            await LoadDropdown(projectId, empName);

            if (!projectId.HasValue)
                return View(new List<ProjectIssue>());

            var issues = await GetIssues(projectId.Value, empName);
            return View(issues);
        }

        // =====================================================
        // DEV INDEX (Programmer page)
        // =====================================================
        [RequireMenu("ProjectIssues.DevIndex")]
        public async Task<IActionResult> DevIndex(int? projectId, string? empName)
        {
            await LoadDropdown(projectId, empName);

            if (!projectId.HasValue)
                return View(new List<ProjectIssue>());

            var issues = await GetIssues(projectId.Value, empName);
            return View(issues);
        }

        // =====================================================
        // DETAILS (VIEW)
        // =====================================================
        [RequireMenu("ProjectIssues.Index")]
        public async Task<IActionResult> Details(int id)
        {
            var issue = await _context.ProjectIssues
                .AsNoTracking()
                .Include(i => i.Project)
                .Include(i => i.Employee)
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null)
                return NotFound();

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
                .Include(i => i.Employee)
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null)
                return NotFound();

            return View(issue);
        }

        // =====================================================
        // VIEW ONLY REPORT
        // =====================================================
        [RequireMenu("ProjectIssues.ViewOnly")]
        public async Task<IActionResult> ViewOnly(int? projectId, string? empName)
        {
            await LoadDropdown(projectId, empName);

            if (!projectId.HasValue)
                return View(new List<ProjectIssue>());

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .Include(i => i.Employee)
                .Where(i => i.ProjectId == projectId.Value)
                .OrderByDescending(i => i.IsReopen)
                .ThenByDescending(i => i.ReopenCount)
                .ThenBy(i => i.IssueId)
                .ToListAsync();

            if (!string.IsNullOrEmpty(empName))
                issues = issues.Where(i => i.Employee != null && i.Employee.EmpName == empName).ToList();

            return View(issues);
        }

        // =====================================================
        // CREATE (GET)
        // =====================================================
        [RequireMenu("ProjectIssues.Create")]
        public IActionResult Create(int projectId)
        {
            var model = new ProjectIssue
            {
                ProjectId = projectId,
                IssueStatus = "OPEN",
                IssuePriority = "NORMAL",
                CreatedAt = DateTime.Now
            };

            ViewBag.ProjectId = projectId;
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
            if (!ModelState.IsValid)
            {
                ViewBag.ProjectId = model.ProjectId;
                ViewBag.Employees = GetEmployeeList(model.EmpId);
                ViewBag.StatusList = GetStatusList(model.IssueStatus);
                return View(model);
            }

            // ✅ normalize (กัน null/ช่องว่าง)
            model.IssueStatus = (model.IssueStatus ?? "OPEN").Trim().ToUpperInvariant();
            model.IssuePriority = (model.IssuePriority ?? "NORMAL").Trim().ToUpperInvariant();
            model.DevStatus = (model.DevStatus ?? "TODO").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(model.DevStatus))
                model.DevStatus = "TODO";

            // ✅ CreatedAt: ถ้าหน้า Create ส่งมาเองก็ใช้ได้ ถ้าไม่ได้ส่งให้ใช้ตอนนี้
            if (model.CreatedAt == default)
                model.CreatedAt = DateTime.Now;

            // ✅ ค่าเริ่มต้น
            model.IsReopen = false;
            model.ReopenCount = 0;
            model.LastFixedAt = null;

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
                ChangedByEmpId = model.EmpId     // ใช้ Owner เป็นคนเปลี่ยน (ถ้ามี user login ค่อยเปลี่ยนเป็นคนสร้างจริง)
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

            return RedirectToAction(nameof(Index), new { projectId = model.ProjectId });
        }

        // =====================================================
        // EDIT (GET)
        // =====================================================
        [RequireMenu("ProjectIssues.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var issue = await _context.ProjectIssues
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null) return NotFound();

            ViewBag.ProjectId = issue.ProjectId;
            ViewBag.Employees = GetEmployeeList(issue.EmpId);
            ViewBag.StatusList = GetStatusList(issue.IssueStatus);

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

            if (!ModelState.IsValid)
            {
                ViewBag.ProjectId = model.ProjectId;
                ViewBag.Employees = GetEmployeeList(model.EmpId);
                ViewBag.StatusList = GetStatusList(model.IssueStatus);
                return View(model);
            }

            var oldStatus = (issue.IssueStatus ?? "").Trim().ToUpperInvariant();
            var newStatus = (model.IssueStatus ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(newStatus))
                newStatus = oldStatus;

            issue.IssueName = model.IssueName;
            issue.IssueDetail = model.IssueDetail;   // BA detail
            issue.EmpId = model.EmpId;
            issue.IssueStatus = newStatus;
            issue.IssuePriority = (model.IssuePriority ?? issue.IssuePriority ?? "NORMAL").Trim().ToUpperInvariant();

            bool wasFixed = oldStatus == "FIXED" || oldStatus == "PASS";
            bool reopened = newStatus == "OPEN" || newStatus == "WIP";

            if (wasFixed && reopened)
            {
                issue.IsReopen = true;
                issue.ReopenCount += 1;

                _context.Entry(issue).Property(x => x.IsReopen).IsModified = true;
                _context.Entry(issue).Property(x => x.ReopenCount).IsModified = true;
            }

            if (newStatus == "FIXED" || newStatus == "PASS")
            {
                issue.LastFixedAt = DateTime.Now;
                _context.Entry(issue).Property(x => x.LastFixedAt).IsModified = true;
            }

            // ✅ insert history เฉพาะตอน status เปลี่ยนจริง
            if (!string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase))
            {
                _context.ProjectIssueStatusHistories.Add(new ProjectIssueStatusHistory
                {
                    IssueId = issue.IssueId,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    IsReopen = issue.IsReopen,
                    ReopenCount = issue.ReopenCount,
                    ChangedAt = DateTime.Now,
                    ChangedByEmpId = issue.EmpId
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
                .Include(i => i.FixImages)
                .FirstOrDefaultAsync(i => i.IssueId == id);

            if (issue == null) return NotFound();

            ViewBag.DevStatusList = GetDevStatusList(issue.DevStatus);
            return View(issue);
        }

        // =====================================================
        // DEV EDIT (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectIssues.DevIndex")]
        public async Task<IActionResult> DevEdit(int id, ProjectIssue model, List<IFormFile>? afterImages, List<int>? deleteFixImageIds)
        {
            if (id != model.IssueId) return NotFound();

            var issue = await _context.ProjectIssues.FirstOrDefaultAsync(i => i.IssueId == id);
            if (issue == null) return NotFound();

            var oldDev = (issue.DevStatus ?? "TODO").Trim().ToUpperInvariant();
            var newDev = (model.DevStatus ?? "TODO").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(newDev)) newDev = "TODO";

            issue.DevStatus = newDev;
            issue.DevDetail = model.DevDetail;   // developer fix detail

            // ✅ set LastFixedAt when dev marks FIXED
            if (!string.Equals(oldDev, "FIXED", StringComparison.OrdinalIgnoreCase)
                && string.Equals(newDev, "FIXED", StringComparison.OrdinalIgnoreCase))
            {
                issue.LastFixedAt = DateTime.Now;
                _context.Entry(issue).Property(x => x.LastFixedAt).IsModified = true;
            }

            _context.Entry(issue).Property(x => x.DevStatus).IsModified = true;
            _context.Entry(issue).Property(x => x.DevDetail).IsModified = true;

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

            return RedirectToAction(nameof(Index), new { projectId = projectId });
        }

        // =====================================================
        // QUERY
        // =====================================================
        private async Task<List<ProjectIssue>> GetIssues(int projectId, string? empName)
        {
            var query = _context.ProjectIssues
                .AsNoTracking()
                .Include(i => i.Images)
                .Include(i => i.FixImages)
                .Include(i => i.Employee)
                .Where(i => i.ProjectId == projectId);

            if (!string.IsNullOrEmpty(empName))
                query = query.Where(i => i.Employee != null && i.Employee.EmpName == empName);

            return await query
                .OrderByDescending(i => i.IsReopen)
                .ThenByDescending(i => i.IssuePriority == "URGENT")
                .ThenBy(i => i.IssueId)
                .ToListAsync();
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

        private async Task LoadDropdown(int? projectId, string? empName)
        {
            ViewBag.Projects = await _context.Projects
                .OrderBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.SelectedEmp = empName;

            if (!projectId.HasValue)
            {
                ViewBag.SelectedProject = null;
                ViewBag.EmpList = new List<string>();
                return;
            }

            var project = await _context.Projects
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

        private SelectList GetStatusList(string? selected = null)
        {
            return new SelectList(
                new[] { "OPEN", "WIP", "FIXED", "REJECT", "PASS", "FAIL" },
                selected
            );
        }

        private SelectList GetDevStatusList(string? selected = null)
        {
            return new SelectList(
                new[] { "TODO", "DOING", "FIXED", "BLOCK" },
                selected
            );
        }

        // =====================================================
        // PRINT VIEW (REPORT)
        // =====================================================
        [RequireMenu("ProjectIssues.ViewOnly")]
        public async Task<IActionResult> Print(int? projectId, string? empName)
        {
            await LoadDropdown(projectId, empName);

            if (!projectId.HasValue)
                return RedirectToAction(nameof(ViewOnly));

            var issues = await GetIssues(projectId.Value, empName);

            return View("Print", issues);
        }
    }
}