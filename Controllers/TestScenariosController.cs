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

namespace ProjectTracking.Controllers
{
    public class TestScenariosController : BaseController
    {
        private readonly AppDbContext _context;

        public TestScenariosController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("TestScenarios.Index")]
        public IActionResult Create(int? projectId, int? groupId)
        {
            ViewBag.Projects = _context.Projects.ToList();
            ViewBag.Groups = _context.TestTemplateGroups
                .Where(g => g.is_active)
                .OrderBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToList();

            ViewBag.SelectedProject = projectId;
            ViewBag.SelectedGroup = groupId;

            return View();
        }

        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Index(int? projectId, int? groupId)
        {
            var scenarios = await _context.TestScenarios
                .Where(x =>
                    (!projectId.HasValue || x.project_id == projectId) &&
                    (!groupId.HasValue || x.group_id == groupId)
                )
                .Join(
                    _context.TestTemplateGroups,
                    s => s.group_id,
                    g => g.group_id,
                    (s, g) => new { s, g }
                )
                .OrderBy(x => x.g.sort_order) // 🔥 เรียงตาม group ก่อน
                .ThenBy(x => x.s.sort_order) // 🔥 แล้วค่อยเรียงใน group
                .ThenBy(x => x.s.scenario_id)
                .Select(x => x.s)
                .ToListAsync();

            var projectGroupIds = scenarios
                .Select(x => x.group_id.GetValueOrDefault())
                .Where(x => x != 0)
                .Distinct()
                .ToList();

            ViewBag.Groups = _context.TestTemplateGroups
                .Where(g => g.is_active && projectGroupIds.Contains(g.group_id))
                .OrderBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToList();
            ViewBag.Projects = _context.Projects.ToList();

            ViewBag.SelectedProject = projectId;
            ViewBag.SelectedGroup = groupId;

            return View(scenarios);
        }

        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Edit(int id)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null) return NotFound();

            ViewBag.Groups = _context.TestTemplateGroups
                .Where(g => g.is_active)
                .OrderBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToList();

            ViewBag.Attachments = await _context.TestScenarioAttachments
                .Where(x => x.ScenarioId == id)
                .ToListAsync();

            return View(scenario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Edit(TestScenario model, List<IFormFile> files, List<int> deleteAttachmentIds)
        {
            model.updated_at = DateTime.Now;

            _context.TestScenarios.Update(model);
            await _context.SaveChangesAsync();

            // ================= SAVE FILE =================
            if (files != null && files.Any())
            {
                var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");

                if (!Directory.Exists(uploadPath))
                    Directory.CreateDirectory(uploadPath);

                foreach (var file in files)
                {
                    if (file.Length > 0)
                    {
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                        var filePath = Path.Combine(uploadPath, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        var attachment = new TestScenarioAttachment
                        {
                            ScenarioId = model.scenario_id,
                            FileName = file.FileName,
                            FilePath = "/uploads/" + fileName,
                            FileType = file.ContentType,
                            FileSize = (int)file.Length,
                            UploadedBy = "system",
                            UploadedAt = DateTime.Now
                        };

                        _context.TestScenarioAttachments.Add(attachment);
                    }
                }

                await _context.SaveChangesAsync();
            }

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

        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> ImportTemplates(int? projectId, int? groupId)
        {
            if (!projectId.HasValue || !groupId.HasValue)
                return RedirectToAction("Index");

            var templates = _context.TestScenarioTemplates
                .Where(t => t.group_id == groupId && t.is_active)
                .ToList();

            var lastCode = _context.TestScenarios
                .Where(x => x.project_id == projectId.Value)
                .OrderByDescending(x => x.scenario_id)
                .Select(x => x.scenario_code)
                .FirstOrDefault();

            int nextNumber = 1;
            if (!string.IsNullOrEmpty(lastCode) && lastCode.Contains("-"))
            {
                var parts = lastCode.Split('-');
                if (int.TryParse(parts.Last(), out int num))
                    nextNumber = num + 1;
            }

            foreach (var t in templates)
            {
                var scenario = new TestScenario
                {
                    project_id = projectId.Value,
                    group_id = t.group_id,
                    scenario_code = $"TC-{nextNumber++:D4}",
                    title = t.title,
                    precondition = t.precondition,
                    steps = t.steps,
                    expected_result = t.expected_result,
                    priority = t.priority_default,
                    status = t.status_default,
                    created_at = DateTime.Now,
                    updated_at = DateTime.Now
                };

                _context.TestScenarios.Add(scenario);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId, groupId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Delete(int id)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null) return NotFound();

            _context.TestScenarios.Remove(scenario);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = scenario.project_id });
        }

        [HttpGet("TestScenarios/PrintReport")]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> PrintReport(int? projectId)
        {
            if (!projectId.HasValue)
                return RedirectToAction("Index");

            var scenarios = await _context.TestScenarios
                .Where(x => x.project_id == projectId)
                .OrderBy(x => x.scenario_id)
                .ToListAsync();

            return View("Print", scenarios);
        }
        [HttpGet]
        [RequireMenu("TestScenarios.Index")]
        public IActionResult ExportPdf(int projectId, List<int> groupIds)
        {
            var data = _context.TestScenarios
                .Where(x =>
                    x.project_id == projectId &&
                    (groupIds == null || groupIds.Count == 0 || groupIds.Contains(x.group_id ?? 0))
                )
                .OrderBy(x => x.scenario_id)
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
                    GroupName = _context.TestTemplateGroups
                        .Where(g => g.group_id == x.group_id)
                        .Select(g => g.group_name)
                        .FirstOrDefault()
                })
                .ToList();

            var project = _context.Projects
                .FirstOrDefault(p => p.ProjectId == projectId);

            var attachments = _context.TestScenarioAttachments
                .Where(a => data.Select(d => d.scenario_id).Contains(a.ScenarioId))
                .ToList();

            var report = new TestScenarioReport();
            var pdf = report.Generate(data, attachments, project?.ProjectName ?? "Project");

            Response.Headers["Content-Disposition"] = "inline; filename=TestScenarioReport.pdf";
            Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";
            return File(pdf, "application/pdf");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Index")]
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

            return RedirectToAction("Index", new { projectId });
        }
        [HttpPost]
        public async Task<IActionResult> UpdateSort([FromBody] List<SortDto> data)
        {
            if (data == null || data.Count == 0)
                return BadRequest();

            foreach (var item in data)
            {
                var scenario = await _context.TestScenarios.FindAsync(item.id);
                if (scenario != null)
                {
                    scenario.sort_order = item.sort;
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        public class SortDto
        {
            public int id { get; set; }
            public int sort { get; set; }
        }
    }
}