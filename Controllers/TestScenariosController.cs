using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        // INDEX (บังคับเลือก Project ก่อน)
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Index(int? groupId, int? projectId)
        {
            ViewBag.Groups = await _context.TestTemplateGroups
                .Where(g => g.is_active)
                .ToListAsync();

            ViewBag.SelectedGroup = groupId;
            ViewBag.Projects = await _context.Projects
                .Select(p => new
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName
                })
                .ToListAsync();
            ViewBag.SelectedProject = projectId;

            if (projectId.HasValue)
            {
                var pname = await _context.Projects
                    .Where(p => p.ProjectId == projectId.Value)
                    .Select(p => p.ProjectName)
                    .FirstOrDefaultAsync();
                ViewBag.SelectedProjectName = pname;
            }

            if (groupId.HasValue)
            {
                var gname = await _context.TestTemplateGroups
                    .Where(g => g.group_id == groupId.Value)
                    .Select(g => g.group_name)
                    .FirstOrDefaultAsync();
                ViewBag.SelectedGroupName = gname;
            }

            if (!projectId.HasValue)
            {
                return View(new List<TestScenario>());
            }

            var scenarios = await _context.TestScenarios
                .Where(x => x.project_id == projectId)
                .OrderBy(x => x.scenario_code)
                .ToListAsync();

            return View(scenarios);
        }

        // =========================
        // CREATE (GET)
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public IActionResult Create(int? projectId, int? groupId)
        {
            ViewBag.Projects = _context.Projects
                .Select(p => new
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName
                })
                .ToList();
            ViewBag.SelectedProject = projectId;
            ViewBag.SelectedGroup = groupId;

            var model = new TestScenario();

            if (projectId.HasValue)
            {
                model.project_id = projectId.Value;
            }

            return View(model);
        }

        // =========================
        // CREATE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Create(TestScenario model, int? groupId)
        {
            // สร้าง TC001 แยกตาม Project (generate ก่อน validation)
            var count = await _context.TestScenarios
                .Where(x => x.project_id == model.project_id)
                .CountAsync();

            model.scenario_code = "TC" + (count + 1).ToString("D3");

            // scenario_code สร้างจากฝั่ง server ไม่ได้กรอกจาก form
            ModelState.Remove("scenario_code");

            if (!ModelState.IsValid)
            {
                ViewBag.Projects = _context.Projects
                    .Select(p => new { ProjectId = p.ProjectId, ProjectName = p.ProjectName })
                    .ToList();
                ViewBag.SelectedProject = model.project_id;
                ViewBag.SelectedGroup = groupId;
                return View(model);
            }

            model.created_at = DateTime.Now;
            model.updated_at = DateTime.Now;

            _context.TestScenarios.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = model.project_id, groupId });
        }

        // =========================
        // EDIT
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Edit(int id, int? groupId)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null) return NotFound();

            ViewBag.Projects = _context.Projects
                .Select(p => new
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName
                })
                .ToList();
            ViewBag.SelectedGroup = groupId;
            return View(scenario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Edit(TestScenario model, int? groupId)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Projects = _context.Projects
                    .Select(p => new { ProjectId = p.ProjectId, ProjectName = p.ProjectName })
                    .ToList();
                ViewBag.SelectedGroup = groupId;
                return View(model);
            }

            model.updated_at = DateTime.Now;

            _context.TestScenarios.Update(model);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = model.project_id, groupId });
        }

        // =========================
        // DELETE
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> Delete(int id, int? groupId)
        {
            var scenario = await _context.TestScenarios.FindAsync(id);
            if (scenario == null) return NotFound();

            _context.TestScenarios.Remove(scenario);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = scenario.project_id, groupId });
        }

        // =========================
        // IMPORT TEMPLATES INTO PROJECT
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> ImportTemplates(int projectId, int? groupId)
        {
            // ดึง template ที่ active เท่านั้น (และกรองตาม Group ถ้ามีการเลือก)
            var templatesQuery = _context.TestScenarioTemplates
                .Where(t => t.is_active);

            if (groupId.HasValue && groupId.Value > 0)
            {
                templatesQuery = templatesQuery.Where(t => t.group_id == groupId.Value);
            }

            var templates = await templatesQuery
                .OrderBy(t => t.template_id)
                .ToListAsync();

            if (!templates.Any())
            {
                return RedirectToAction("Index", new { projectId, groupId });
            }

            // หา TC ล่าสุดของ project
            var count = await _context.TestScenarios
                .Where(x => x.project_id == projectId)
                .CountAsync();

            int running = count;

            foreach (var t in templates)
            {
                running++;

                var scenario = new TestScenario
                {
                    project_id = projectId,
                    scenario_code = "TC" + running.ToString("D3"),
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

        // =========================
        // DELETE ALL SCENARIOS IN PROJECT
        // =========================
        [RequireMenu("TestScenarios.Index")]
        public async Task<IActionResult> DeleteAll(int projectId, int? groupId)
        {
            var scenarios = await _context.TestScenarios
                .Where(x => x.project_id == projectId)
                .ToListAsync();

            if (scenarios.Any())
            {
                _context.TestScenarios.RemoveRange(scenarios);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index", new { projectId, groupId });
        }

        // =========================
        // PRINTABLE QA REPORT
        // =========================
        [RequireMenu("TestScenarios.ViewOnly")]
        public async Task<IActionResult> PrintReport(int projectId, int? groupId)
        {
            var scenarios = await _context.TestScenarios
                .Where(x => x.project_id == projectId)
                .OrderBy(x => x.scenario_code)
                .ToListAsync();

            var projectName = await _context.Projects
                .Where(p => p.ProjectId == projectId)
                .Select(p => p.ProjectName)
                .FirstOrDefaultAsync();

            string? groupName = null;
            if (groupId.HasValue)
            {
                groupName = await _context.TestTemplateGroups
                    .Where(g => g.group_id == groupId.Value)
                    .Select(g => g.group_name)
                    .FirstOrDefaultAsync();
            }

            ViewBag.ProjectName = projectName;
            ViewBag.GroupName = groupName;
            ViewBag.ProjectId = projectId;
            ViewBag.GroupId = groupId;

            return View("Print", scenarios);
        }
    }
}