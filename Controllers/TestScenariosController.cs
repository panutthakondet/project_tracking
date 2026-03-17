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

namespace ProjectTracking.Controllers
{
    public class TestScenariosController : BaseController
    {
        private readonly AppDbContext _context;

        public TestScenariosController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // CREATE (GET)
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public IActionResult Create(int? projectId, int? groupId)
        {
            ViewBag.Projects = _context.Projects.ToList();
            ViewBag.Groups = _context.TestTemplateGroups
                .Where(g => g.is_active)
                .ToList();

            ViewBag.SelectedProject = projectId;
            ViewBag.SelectedGroup = groupId;

            return View();
        }

        // =========================
        // INDEX (FIX 404)
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Index(int? projectId, int? groupId)
        {
            var scenarios = await _context.TestScenarios
                .Where(x =>
                    (!projectId.HasValue || x.project_id == projectId) &&
                    (!groupId.HasValue || x.group_id == groupId)
                )
                .OrderBy(x => x.scenario_id)
                .ToListAsync();

            // กัน null
            if (scenarios == null)
                scenarios = new List<TestScenario>();

            // กัน ViewBag null ที่ View ใช้
            ViewBag.Groups = _context.TestTemplateGroups
                .Where(g => g.is_active)
                .ToList();

            // 🔥 restore Project dropdown
            ViewBag.Projects = _context.Projects
                .ToList();

            // 🔥 keep selected values
            ViewBag.SelectedProject = projectId;
            ViewBag.SelectedGroup = groupId;

            return View(scenarios);
        }

        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Edit(int id, int? groupId)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null) return NotFound();

            ViewBag.Groups = _context.TestTemplateGroups.Where(g => g.is_active).ToList();

            // โหลดรูป
            var attachments = await _context.TestScenarioAttachments
                .Where(x => x.ScenarioId == id)
                .ToListAsync();

            ViewBag.Attachments = attachments;

            return View(scenario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Edit(TestScenario model, int? groupId, List<IFormFile> files, List<int> deleteAttachmentIds)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.updated_at = DateTime.Now;

            _context.TestScenarios.Update(model);
            await _context.SaveChangesAsync();

            // 🔥 ลบรูปที่ติ๊ก
            if (deleteAttachmentIds != null && deleteAttachmentIds.Any())
            {
                var items = _context.TestScenarioAttachments
                    .Where(x => deleteAttachmentIds.Contains(x.AttachmentId))
                    .ToList();

                foreach (var item in items)
                {
                    // ลบไฟล์จริง
                    var relativePath = item.FilePath ?? "";
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }

                    _context.TestScenarioAttachments.Remove(item);
                }

                await _context.SaveChangesAsync();
            }

            // 🔥 upload รูป
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    if (file.Length > 0)
                    {
                        var folder = Path.Combine(Directory.GetCurrentDirectory(),
                            "wwwroot/uploads/testcase",
                            model.scenario_id.ToString());

                        if (!Directory.Exists(folder))
                            Directory.CreateDirectory(folder);

                        var fileName = Path.GetFileName(file.FileName);
                        var filePath = Path.Combine(folder, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        var attachment = new TestScenarioAttachment
                        {
                            ScenarioId = model.scenario_id,
                            FileName = fileName,
                            FilePath = "/uploads/testcase/" + model.scenario_id + "/" + fileName,
                            FileType = file.ContentType,
                            FileSize = (int)file.Length,
                            UploadedAt = DateTime.Now
                        };

                        _context.TestScenarioAttachments.Add(attachment);
                    }
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index", new { projectId = model.project_id });
        }
        // =========================
        // IMPORT TEMPLATES
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> ImportTemplates(int? projectId, int? groupId)
        {
            if (!projectId.HasValue || !groupId.HasValue)
            {
                return RedirectToAction("Index");
            }

            var templates = _context.TestScenarioTemplates
                .Where(t => t.group_id == groupId && t.is_active)
                .ToList();

            // 🔥 get last running number once
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
                {
                    nextNumber = num + 1;
                }
            }

            foreach (var t in templates)
            {
                var newCode = $"TC-{nextNumber:D4}";
                nextNumber++;

                var scenario = new TestScenario
                {
                    project_id = projectId.Value,
                    group_id = t.group_id,
                    scenario_code = newCode,
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

            return RedirectToAction("Index", new { projectId = projectId, groupId = groupId });
        }
        // =========================
        // DELETE (GET FIX 405)
        // =========================
        [HttpGet("TestScenarios/Delete/{id}")]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> DeleteGet(int id)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null)
            {
                return NotFound();
            }

            // ลบ attachments ก่อน (ถ้ามี)
            var attachments = _context.TestScenarioAttachments
                .Where(x => x.ScenarioId == id)
                .ToList();

            foreach (var item in attachments)
            {
                var relativePath = item.FilePath ?? "";
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.TrimStart('/'));

                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }

                _context.TestScenarioAttachments.Remove(item);
            }

            _context.TestScenarios.Remove(scenario);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = scenario.project_id });
        }
        // =========================
        // DELETE
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Delete(int id)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null)
            {
                return NotFound();
            }

            // ลบ attachments ก่อน (ถ้ามี)
            var attachments = _context.TestScenarioAttachments
                .Where(x => x.ScenarioId == id)
                .ToList();

            foreach (var item in attachments)
            {
                var relativePath = item.FilePath ?? "";
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.TrimStart('/'));

                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }

                _context.TestScenarioAttachments.Remove(item);
            }

            _context.TestScenarios.Remove(scenario);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = scenario.project_id });
        }
    }
}