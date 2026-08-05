using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectTracking.Reports;
using Microsoft.AspNetCore.Hosting;
using ProjectTracking.Helpers;

namespace ProjectTracking.Controllers
{
    public class TestScenariosController : BaseController
    {
        private const string IndexStatusFilterKey = "TestScenarios.Filter.Status";
        private static readonly (string Value, string Text)[] ScenarioStatusFilters =
        {
            ("READY", "พร้อมทดสอบ"),
            ("PASSED", "ผ่าน"),
            ("FAILED", "ไม่ผ่าน")
        };

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public TestScenariosController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [RequireMenu("TestScenarios.Create")]
        public async Task<IActionResult> Create(int? projectId, int? groupId)
        {
            await LoadScenarioFormListsAsync(projectId, groupId);

            return View(new TestScenario
            {
                project_id = projectId ?? 0,
                group_id = groupId,
                priority = "MEDIUM",
                status = "READY"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Create")]
        public async Task<IActionResult> Create(TestScenario model, List<IFormFile> files)
        {
            ModelState.Remove(nameof(TestScenario.scenario_code));

            if (model.project_id <= 0)
                ModelState.AddModelError(nameof(TestScenario.project_id), "กรุณาเลือกโครงการ");

            if (!model.group_id.HasValue || model.group_id.Value <= 0)
                ModelState.AddModelError(nameof(TestScenario.group_id), "กรุณาเลือกกลุ่ม Test Scenario");

            if (model.project_id > 0)
            {
                var projectExists = await _context.Projects.AnyAsync(x => x.ProjectId == model.project_id);
                if (!projectExists)
                    ModelState.AddModelError(nameof(TestScenario.project_id), "ไม่พบโครงการที่เลือก");
            }

            if (model.group_id.HasValue && model.group_id.Value > 0)
            {
                var groupExists = await _context.TestTemplateGroups
                    .AnyAsync(x => x.group_id == model.group_id.Value && x.is_active);
                if (!groupExists)
                    ModelState.AddModelError(nameof(TestScenario.group_id), "ไม่พบกลุ่ม Test Scenario ที่เลือก");
            }

            if (!ModelState.IsValid)
            {
                await LoadScenarioFormListsAsync(
                    model.project_id > 0 ? model.project_id : null,
                    model.group_id);
                return View(model);
            }

            var nextNumber = await GetNextScenarioNumberAsync(model.project_id);
            var nextSort = await _context.TestScenarios
                .Where(x => x.project_id == model.project_id && x.group_id == model.group_id)
                .Select(x => (int?)x.sort_order)
                .MaxAsync() ?? 0;

            model.scenario_code = $"TC-{nextNumber:D4}";
            model.sort_order = nextSort + 1;
            model.priority = string.IsNullOrWhiteSpace(model.priority) ? "MEDIUM" : model.priority.Trim().ToUpperInvariant();
            model.status = TestScenarioDisplay.NormalizeStatus(model.status);
            model.created_at = DateTime.Now;
            model.updated_at = DateTime.Now;

            _context.TestScenarios.Add(model);
            await _context.SaveChangesAsync();

            await SaveScenarioAttachmentsAsync(model, files);
            await RenumberScenarioCodesAsync(model.project_id);

            return RedirectToAction("Index", new { projectId = model.project_id, groupId = model.group_id });
        }

        private async Task LoadScenarioFormListsAsync(int? selectedProject, int? selectedGroup)
        {
            ViewBag.Projects = await _context.Projects
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();
            ViewBag.Groups = await _context.TestTemplateGroups
                .Include(g => g.Control)
                .Where(g => g.is_active)
                .OrderBy(g => g.Control != null ? g.Control.sort_order : int.MaxValue)
                .ThenBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToListAsync();

            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedGroup = selectedGroup;
        }

        private async Task SaveScenarioAttachmentsAsync(TestScenario model, List<IFormFile>? files)
        {
            if (files == null || files.Count == 0)
                return;

            var projectFolder = Path.Combine(
                _env.WebRootPath,
                "uploads",
                "testcase",
                model.project_id.ToString()
            );

            if (!Directory.Exists(projectFolder))
                Directory.CreateDirectory(projectFolder);

            foreach (var file in files)
            {
                if (file.Length <= 0)
                    continue;

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                var filePath = Path.Combine(projectFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var attachment = new TestScenarioAttachment
                {
                    ScenarioId = model.scenario_id,
                    FileName = file.FileName,
                    FilePath = $"/uploads/testcase/{model.project_id}/{fileName}",
                    FileType = file.ContentType,
                    FileSize = (int)file.Length,
                    UploadedBy = "system",
                    UploadedAt = DateTime.Now
                };

                _context.TestScenarioAttachments.Add(attachment);
            }

            await _context.SaveChangesAsync();
        }

        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Index(int? projectId, int? groupId, List<int>? groupIds, string? status, string? coopName)
        {
            var selectedStatus = ResolveIndexStatusFilter(status);
            var selectedCoopName = (coopName ?? "").Trim();
            var projects = await _context.Projects
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();
            var coopOptions = projects
                .Select(p => p.Coop?.CoopName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();
            if (!string.IsNullOrWhiteSpace(selectedCoopName) &&
                !coopOptions.Any(name => string.Equals(name, selectedCoopName, StringComparison.OrdinalIgnoreCase)))
            {
                selectedCoopName = "";
            }
            var selectedCoopProjectIds = string.IsNullOrWhiteSpace(selectedCoopName)
                ? new List<int>()
                : projects
                    .Where(p => string.Equals(p.Coop?.CoopName, selectedCoopName, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.ProjectId)
                    .ToList();
            var selectedGroupIds = (groupIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (selectedGroupIds.Count == 0 && groupId.HasValue && groupId.Value > 0)
                selectedGroupIds.Add(groupId.Value);

            var scenarios = await _context.TestScenarios
                .Include(x => x.Group)
                    .ThenInclude(x => x!.Control)
                .Where(x =>
                    (!projectId.HasValue || x.project_id == projectId) &&
                    (string.IsNullOrWhiteSpace(selectedCoopName) || selectedCoopProjectIds.Contains(x.project_id)) &&
                    (selectedGroupIds.Count == 0 || (x.group_id.HasValue && selectedGroupIds.Contains(x.group_id.Value))) &&
                    (string.IsNullOrWhiteSpace(selectedStatus) || x.status == selectedStatus)
                )
                .OrderBy(x => x.Group != null && x.Group.Control != null ? x.Group.Control.sort_order : int.MaxValue)
                .ThenBy(x => x.Group != null ? x.Group.sort_order : int.MaxValue)
                .ThenBy(x => x.sort_order)
                .ThenBy(x => x.scenario_id)
                .ToListAsync();

            ViewBag.Groups = _context.TestTemplateGroups
                .Include(g => g.Control)
                .Where(g => g.is_active)
                .OrderBy(g => g.Control != null ? g.Control.sort_order : int.MaxValue)
                .ThenBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToList();
            ViewBag.Projects = projects;
            ViewBag.CoopOptions = coopOptions;

            ViewBag.SelectedProject = projectId;
            ViewBag.SelectedCoopName = selectedCoopName;
            ViewBag.SelectedGroup = selectedGroupIds.Count == 1 ? (int?)selectedGroupIds[0] : null;
            ViewBag.SelectedGroupIds = selectedGroupIds;
            ViewBag.StatusList = ScenarioStatusFilters;
            ViewBag.SelectedStatus = selectedStatus;

            return View(scenarios);
        }

        [RequireMenu("TestScenarios.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null) return NotFound();

            ViewBag.Projects = await _context.Projects
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.Groups = _context.TestTemplateGroups
                .Include(g => g.Control)
                .Where(g => g.is_active)
                .OrderBy(g => g.Control != null ? g.Control.sort_order : int.MaxValue)
                .ThenBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToList();

            ViewBag.Attachments = await _context.TestScenarioAttachments
                .Where(x => x.ScenarioId == id)
                .ToListAsync();

            return View(scenario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Edit")]
        public async Task<IActionResult> Edit(TestScenario model, List<IFormFile> files, List<int> deleteAttachmentIds)
        {
            model.updated_at = DateTime.Now;

            _context.TestScenarios.Update(model);
            await _context.SaveChangesAsync();

            // ================= SAVE FILE =================
            var projectFolder = Path.Combine(
                _env.WebRootPath,
                "uploads",
                "testcase",
                model.project_id.ToString()
            );

            if (!Directory.Exists(projectFolder))
                Directory.CreateDirectory(projectFolder);

            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                    var filePath = Path.Combine(projectFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var attachment = new TestScenarioAttachment
                    {
                        ScenarioId = model.scenario_id,
                        FileName = file.FileName,
                        FilePath = $"/uploads/testcase/{model.project_id}/{fileName}",
                        FileType = file.ContentType,
                        FileSize = (int)file.Length,
                        UploadedBy = "system",
                        UploadedAt = DateTime.Now
                    };

                    _context.TestScenarioAttachments.Add(attachment);
                }
            }

            await _context.SaveChangesAsync();

            if (deleteAttachmentIds != null && deleteAttachmentIds.Any())
            {
                var items = _context.TestScenarioAttachments
                    .Where(x => deleteAttachmentIds.Contains(x.AttachmentId))
                    .ToList();

                foreach (var item in items)
                {
                    var relativePath = item.FilePath ?? "";
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.TrimStart('/'));

                    if (System.IO.File.Exists(fullPath))
                        System.IO.File.Delete(fullPath);

                    _context.TestScenarioAttachments.Remove(item);
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index", new { projectId = model.project_id });
        }

        [HttpGet]
        [RequireMenu("TestScenarios.Import")]
        public async Task<IActionResult> ImportTemplates(int? projectId, int? groupId)
        {
            if (!projectId.HasValue || !groupId.HasValue)
                return RedirectToAction("Index");

            var result = await ImportTemplatesForGroupsAsync(projectId.Value, new[] { groupId.Value });
            SetImportTemplatesMessage(result);

            return RedirectToAction("Index", new { projectId, groupId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Import")]
        public async Task<IActionResult> ImportTemplates(int? projectId, List<int> groupIds)
        {
            if (!projectId.HasValue || groupIds == null || groupIds.Count == 0)
            {
                TempData["Error"] = "กรุณาเลือก Group อย่างน้อย 1 รายการก่อน Import";
                return RedirectToAction("Index", new { projectId });
            }

            var result = await ImportTemplatesForGroupsAsync(projectId.Value, groupIds);
            SetImportTemplatesMessage(result);

            return RedirectToAction("Index", new { projectId });
        }

        private async Task<ImportTemplatesResult> ImportTemplatesForGroupsAsync(int projectId, IEnumerable<int> groupIds)
        {
            var selectedGroupIds = groupIds
                .Distinct()
                .ToList();

            if (selectedGroupIds.Count == 0)
                return new ImportTemplatesResult(0, 0);

            var orderedGroupIds = await _context.TestTemplateGroups
                .AsNoTracking()
                .Include(g => g.Control)
                .Where(g => g.is_active && selectedGroupIds.Contains(g.group_id))
                .OrderBy(g => g.Control != null ? g.Control.sort_order : int.MaxValue)
                .ThenBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .Select(g => g.group_id)
                .ToListAsync();

            if (orderedGroupIds.Count == 0)
                return new ImportTemplatesResult(0, 0);

            var templates = await _context.TestScenarioTemplates
                .AsNoTracking()
                .Where(t => t.is_active && t.group_id.HasValue && orderedGroupIds.Contains(t.group_id.Value))
                .OrderBy(t => t.template_id)
                .ToListAsync();

            var existingScenarioRows = await _context.TestScenarios
                .AsNoTracking()
                .Where(s => s.project_id == projectId
                    && s.group_id.HasValue
                    && orderedGroupIds.Contains(s.group_id.Value))
                .Select(s => new { s.group_id, s.title })
                .ToListAsync();
            var existingScenarioKeys = existingScenarioRows
                .Select(row => BuildScenarioDuplicateKey(row.group_id, row.title))
                .ToHashSet(StringComparer.Ordinal);

            var nextNumber = await GetNextScenarioNumberAsync(projectId);
            var importedCount = 0;
            var skippedCount = 0;

            foreach (var selectedGroupId in orderedGroupIds)
            {
                foreach (var t in templates.Where(t => t.group_id == selectedGroupId))
                {
                    var duplicateKey = BuildScenarioDuplicateKey(t.group_id, t.title);
                    if (!existingScenarioKeys.Add(duplicateKey))
                    {
                        skippedCount++;
                        continue;
                    }

                    var scenario = new TestScenario
                    {
                        project_id = projectId,
                        group_id = t.group_id,
                        scenario_code = $"TC-{nextNumber++:D4}",
                        title = t.title,
                        precondition = t.precondition,
                        steps = t.steps,
                        expected_result = t.expected_result,
                        priority = t.priority_default,
                        status = TestScenarioDisplay.NormalizeStatus(t.status_default),
                        created_at = DateTime.Now,
                        updated_at = DateTime.Now
                    };

                    _context.TestScenarios.Add(scenario);
                    importedCount++;
                }
            }

            if (importedCount > 0)
            {
                await _context.SaveChangesAsync();
                await RenumberScenarioCodesAsync(projectId);
            }

            return new ImportTemplatesResult(importedCount, skippedCount);
        }

        private static string BuildScenarioDuplicateKey(int? groupId, string? title)
        {
            var normalizedTitle = string.Join(" ", (title ?? string.Empty)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Trim()
                .ToUpperInvariant();
            return $"{groupId?.ToString() ?? "-"}|{normalizedTitle}";
        }

        private void SetImportTemplatesMessage(ImportTemplatesResult result)
        {
            if (result.ImportedCount > 0)
            {
                TempData["Success"] = result.SkippedCount > 0
                    ? $"Import สำเร็จ {result.ImportedCount} รายการ และข้ามรายการซ้ำ {result.SkippedCount} รายการ"
                    : $"Import สำเร็จ {result.ImportedCount} รายการ";
                return;
            }

            TempData["Warning"] = result.SkippedCount > 0
                ? $"ไม่มีรายการใหม่ ข้ามรายการซ้ำทั้งหมด {result.SkippedCount} รายการ"
                : "ไม่พบ Template ที่พร้อม Import ใน Group ที่เลือก";
        }

        private sealed record ImportTemplatesResult(int ImportedCount, int SkippedCount);

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null) return NotFound();

            _context.TestScenarios.Remove(scenario);
            await _context.SaveChangesAsync();
            await RenumberScenarioCodesAsync(scenario.project_id);

            return RedirectToAction("Index", new { projectId = scenario.project_id });
        }

        [HttpGet("TestScenarios/PrintReport")]
        [RequireMenu("TestScenarios.Export")]
        public async Task<IActionResult> PrintReport(int? projectId, int? groupId, string? status, string? priority)
        {
            var selectedStatus = ResolveIndexStatusFilter(status);
            var projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .OrderBy(x => x.Coop != null ? x.Coop.CoopName : "")
                .ThenBy(x => x.ProjectName)
                .ToListAsync();

            var allScenarios = await _context.TestScenarios
                .AsNoTracking()
                .Include(x => x.Group)
                    .ThenInclude(x => x!.Control)
                .ToListAsync();

            var groupQuery = _context.TestTemplateGroups
                .AsNoTracking()
                .Include(x => x.Control)
                .AsQueryable();

            if (projectId.HasValue)
            {
                var projectGroupIds = allScenarios
                    .Where(x => x.project_id == projectId.Value && x.group_id.HasValue)
                    .Select(x => x.group_id!.Value)
                    .Distinct()
                    .ToList();

                groupQuery = groupQuery.Where(x => projectGroupIds.Contains(x.group_id));
            }

            var groups = await groupQuery
                .OrderBy(x => x.Control != null ? x.Control.sort_order : int.MaxValue)
                .ThenBy(x => x.sort_order)
                .ThenBy(x => x.group_name)
                .ToListAsync();

            if (groupId.HasValue && !groups.Any(x => x.group_id == groupId.Value))
                groupId = null;

            var statusList = ScenarioStatusFilters
                .Select(x => x.Value)
                .ToList();

            var priorityList = allScenarios
                .Select(x => x.priority)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            var result = allScenarios.AsEnumerable();

            if (projectId.HasValue)
                result = result.Where(x => x.project_id == projectId.Value);

            if (groupId.HasValue)
                result = result.Where(x => x.group_id == groupId.Value);

            if (!string.IsNullOrWhiteSpace(selectedStatus))
            {
                result = result.Where(x => string.Equals(TestScenarioDisplay.NormalizeStatus(x.status), selectedStatus, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(priority))
                result = result.Where(x => string.Equals(x.priority, priority, StringComparison.OrdinalIgnoreCase));

            ViewBag.Projects = projects;
            ViewBag.Groups = groups;
            ViewBag.StatusList = statusList;
            ViewBag.PriorityList = priorityList;
            ViewBag.SelectedProject = projectId;
            ViewBag.SelectedGroup = groupId;
            ViewBag.SelectedStatus = selectedStatus;
            ViewBag.SelectedPriority = priority;
            ViewBag.ProjectNames = projects.ToDictionary(x => x.ProjectId, x => x.ProjectDisplayName);

            var scenarios = result
                .OrderBy(x => projects.FindIndex(p => p.ProjectId == x.project_id) < 0 ? int.MaxValue : projects.FindIndex(p => p.ProjectId == x.project_id))
                .ThenBy(x => x.Group?.Control?.sort_order ?? int.MaxValue)
                .ThenBy(x => x.Group?.sort_order ?? int.MaxValue)
                .ThenBy(x => x.sort_order)
                .ThenBy(x => x.scenario_id)
                .ToList();

            return View("Print", scenarios);
        }
        [HttpGet]
        [RequireMenu("TestScenarios.Export")]
        public IActionResult ExportPdf(int projectId, List<int> groupIds, string? status)
        {
            var selectedStatus = NormalizeIndexStatus(status);
            var data = _context.TestScenarios
                .Include(x => x.Group)
                    .ThenInclude(x => x!.Control)
                .Where(x =>
                    x.project_id == projectId &&
                    (
                        groupIds == null ||
                        groupIds.Count == 0 ||
                        (x.group_id.HasValue && groupIds.Contains(x.group_id.Value))
                    ) &&
                    (string.IsNullOrWhiteSpace(selectedStatus) || x.status == selectedStatus)
                )
                .OrderBy(x => x.Group != null && x.Group.Control != null ? x.Group.Control.sort_order : int.MaxValue)
                .ThenBy(x => x.Group != null ? x.Group.sort_order : int.MaxValue)
                .ThenBy(x => x.sort_order)
                .ThenBy(x => x.scenario_id)
                .Select(x => new TestScenario
                {
                    scenario_id = x.scenario_id,
                    project_id = x.project_id,
                    group_id = x.group_id,
                    scenario_code = x.scenario_code,
                    title = x.title,
                    precondition = x.precondition,
                    steps = x.steps,
                    expected_result = x.expected_result,
                    remark = x.remark,
                    priority = x.priority,
                    status = x.status,
                    created_at = x.created_at,
                    updated_at = x.updated_at,

                    // 🔥 ดึงชื่อ Group
                    GroupName = x.Group == null
                        ? "ไม่ระบุ Group"
                        : $"{(x.Group.Control != null ? x.Group.Control.control_name : "ยังไม่กำหนด Control")} / {x.Group.group_name}"
                })
                .ToList();

            var project = _context.Projects
                .Include(p => p.Coop)
                .FirstOrDefault(p => p.ProjectId == projectId);

            var attachments = _context.TestScenarioAttachments
                .Where(a => data.Select(d => d.scenario_id).Contains(a.ScenarioId))
                .ToList();

            var report = new TestScenarioReport();
            var pdf = report.Generate(
                data,
                attachments,
                project?.ProjectDisplayName ?? "Project",
                _env.WebRootPath
            );

            Response.Headers["Content-Disposition"] = "inline; filename=TestScenarioReport.pdf";
            Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";
            return File(pdf, "application/pdf");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.DeleteAll")]
        public async Task<IActionResult> DeleteAll(int projectId)
        {
            var scenarios = _context.TestScenarios
                .Where(x => x.project_id == projectId)
                .ToList();

            if (!scenarios.Any())
                return RedirectToAction("Index", new { projectId });

            var scenarioIds = scenarios.Select(s => s.scenario_id).ToList();

            var attachments = _context.TestScenarioAttachments
                .Where(a => scenarioIds.Contains(a.ScenarioId))
                .ToList();

            foreach (var item in attachments)
            {
                var fullPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    (item.FilePath ?? "").TrimStart('/')
                );

                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }

            _context.TestScenarioAttachments.RemoveRange(attachments);
            _context.TestScenarios.RemoveRange(scenarios);

            await _context.SaveChangesAsync();
            await RenumberScenarioCodesAsync(projectId);

            return RedirectToAction("Index", new { projectId });
        }
        [HttpPost]
        [RequireMenu("TestScenarios.Sort")]
        public async Task<IActionResult> UpdateSort([FromBody] List<SortDto> data)
        {
            if (data == null || data.Count == 0)
                return BadRequest();

            var projectIds = new HashSet<int>();

            foreach (var item in data)
            {
                var scenario = await _context.TestScenarios.FindAsync(item.id);
                if (scenario != null)
                {
                    scenario.sort_order = item.sort;
                    projectIds.Add(scenario.project_id);
                }
            }

            await _context.SaveChangesAsync();

            foreach (var projectId in projectIds)
            {
                await RenumberScenarioCodesAsync(projectId);
            }

            return Ok();
        }

        private string ResolveIndexStatusFilter(string? status)
        {
            if (!Request.Query.ContainsKey("status"))
                return NormalizeIndexStatus(HttpContext.Session.GetString(IndexStatusFilterKey));

            var selectedStatus = NormalizeIndexStatus(status);
            if (string.IsNullOrWhiteSpace(selectedStatus))
            {
                HttpContext.Session.Remove(IndexStatusFilterKey);
                return "";
            }

            HttpContext.Session.SetString(IndexStatusFilterKey, selectedStatus);
            return selectedStatus;
        }

        private static string NormalizeIndexStatus(string? status)
        {
            var value = (status ?? "").Trim().ToUpperInvariant();
            return ScenarioStatusFilters.Any(x => x.Value == value) ? value : "";
        }

        private async Task RenumberScenarioCodesAsync(int projectId)
        {
            var scenarios = await _context.TestScenarios
                .Include(x => x.Group)
                    .ThenInclude(x => x!.Control)
                .Where(x => x.project_id == projectId)
                .OrderBy(x => x.Group != null && x.Group.Control != null ? x.Group.Control.sort_order : int.MaxValue)
                .ThenBy(x => x.Group != null ? x.Group.sort_order : int.MaxValue)
                .ThenBy(x => x.Group != null ? x.Group.group_name : string.Empty)
                .ThenBy(x => x.sort_order)
                .ThenBy(x => x.scenario_id)
                .ToListAsync();

            var number = 1;
            var changed = false;

            foreach (var scenario in scenarios)
            {
                var nextCode = $"TC-{number++:D4}";
                if (string.Equals(scenario.scenario_code, nextCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                scenario.scenario_code = nextCode;
                scenario.updated_at = DateTime.Now;
                changed = true;
            }

            if (changed)
            {
                foreach (var scenario in scenarios)
                {
                    scenario.scenario_code = $"TMP{scenario.scenario_id:D7}";
                    scenario.updated_at = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                number = 1;
                foreach (var scenario in scenarios)
                {
                    scenario.scenario_code = $"TC-{number++:D4}";
                    scenario.updated_at = DateTime.Now;
                }

                await _context.SaveChangesAsync();
            }
        }

        private async Task<int> GetNextScenarioNumberAsync(int projectId)
        {
            var codes = await _context.TestScenarios
                .AsNoTracking()
                .Where(x => x.project_id == projectId)
                .Select(x => x.scenario_code)
                .ToListAsync();

            var maxNumber = 0;
            foreach (var code in codes)
            {
                if (string.IsNullOrWhiteSpace(code) || !code.Contains("-"))
                    continue;

                var parts = code.Split('-');
                if (int.TryParse(parts.Last(), out var number) && number > maxNumber)
                    maxNumber = number;
            }

            return maxNumber + 1;
        }

        public class SortDto
        {
            public int id { get; set; }
            public int sort { get; set; }
        }
    }
}
