using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class TestScenarioTemplatesController : BaseController
    {
        private readonly AppDbContext _context;

        public TestScenarioTemplatesController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // INDEX
        // =========================
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> Index(int? groupId)
        {
            if (!groupId.HasValue && TempData["LastGroupId"] != null)
            {
                groupId = Convert.ToInt32(TempData["LastGroupId"]);
            }

            var query = _context.TestScenarioTemplates
                .Include(x => x.Group)
                .AsQueryable();

            if (groupId.HasValue)
            {
                query = query.Where(x => x.group_id == groupId);
            }

            var templates = await query
                .OrderBy(x => x.template_id)
                .ToListAsync();

            ViewBag.Groups = await _context.TestTemplateGroups
                .Where(g => g.is_active)
                .OrderBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToListAsync();
            ViewBag.SelectedGroupId = groupId;
            ViewBag.GroupId = groupId;
            return View(templates);
        }

        // =========================
        // CREATE (GET)
        // =========================
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> Create(int? groupId)
        {
            ViewBag.Groups = await _context.TestTemplateGroups
                .Where(g => g.is_active)
                .OrderBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToListAsync();

            // If coming from Index with selected group, preselect and lock it.
            ViewBag.LockGroup = groupId.HasValue;

            var model = new TestScenarioTemplate
            {
                group_id = groupId
            };

            return View(model);
        }

        // =========================
        // CREATE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> Create(TestScenarioTemplate model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Groups = await _context.TestTemplateGroups
                    .Where(g => g.is_active)
                    .OrderBy(g => g.sort_order)
                    .ThenBy(g => g.group_name)
                    .ToListAsync();

                // If group_id was provided (e.g., from Index), lock it in the UI
                ViewBag.LockGroup = model.group_id.HasValue;

                return View(model);
            }

            if (!model.group_id.HasValue || model.group_id.Value <= 0)
            {
                ModelState.AddModelError("group_id", "กรุณาเลือก Template Group");

                ViewBag.Groups = await _context.TestTemplateGroups
                    .Where(g => g.is_active)
                    .OrderBy(g => g.group_name)
                    .ToListAsync();

                ViewBag.LockGroup = false;
                return View(model);
            }

            model.created_at = DateTime.Now;
            model.updated_at = DateTime.Now;
            model.is_active = true;

            _context.TestScenarioTemplates.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { groupId = model.group_id });
        }

        // =========================
        // EDIT (GET)
        // =========================
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> Edit(int id)
        {
            var template = await _context.TestScenarioTemplates
                .FirstOrDefaultAsync(x => x.template_id == id);

            if (template == null)
                return NotFound();

            ViewBag.Groups = await _context.TestTemplateGroups
                .Where(g => g.is_active)
                .OrderBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToListAsync();

            TempData["LastGroupId"] = template.group_id;

            return View(template);
        }

        // =========================
        // EDIT (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> Edit(TestScenarioTemplate model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Groups = await _context.TestTemplateGroups
                    .Where(g => g.is_active)
                    .OrderBy(g => g.sort_order)
                    .ThenBy(g => g.group_name)
                    .ToListAsync();

                // Keep group locked in UI (Edit page always locked)
                ViewBag.LockGroup = true;
                return View(model);
            }

            var template = await _context.TestScenarioTemplates
                .FirstOrDefaultAsync(x => x.template_id == model.template_id);

            if (template == null)
                return NotFound();

            // template.group_id = model.group_id; // removed to lock group server-side
            template.title = model.title;
            template.precondition = model.precondition;
            template.steps = model.steps;
            template.expected_result = model.expected_result;
            template.priority_default = model.priority_default;
            template.status_default = model.status_default;
            template.updated_at = DateTime.Now;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { groupId = template.group_id });
        }

        // =========================
        // DELETE
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> Delete(int id)
        {
            var template = await _context.TestScenarioTemplates
                .FirstOrDefaultAsync(x => x.template_id == id);

            if (template == null)
                return NotFound();

            var gid = template.group_id;

            _context.TestScenarioTemplates.Remove(template);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { groupId = gid });
        }

        // =========================
        // TOGGLE ACTIVE
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var template = await _context.TestScenarioTemplates
                .FirstOrDefaultAsync(x => x.template_id == id);

            if (template == null)
                return NotFound();

            var gid = template.group_id;

            template.is_active = !template.is_active;
            template.updated_at = DateTime.Now;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { groupId = gid });
        }
    }
}