using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Helpers;

namespace ProjectTracking.Controllers
{
    public class TestScenarioTemplatesController : BaseController
    {
        private const string IndexDepartmentFilterKey = "TestScenarioTemplates.Filter.DepartmentId";
        private readonly AppDbContext _context;

        public TestScenarioTemplatesController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // INDEX
        // =========================
        [RequireMenu("TestScenarioTemplates.Index")]
        public async Task<IActionResult> Index(int? departmentId, int? controlId, int? groupId)
        {
            var departments = await _context.ProjectDepartments
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DepartmentName)
                .ToListAsync();
            var selectedDepartmentId = ResolveIndexDepartmentFilter(
                departmentId,
                departments.Select(x => x.DepartmentId).ToHashSet());

            if (!groupId.HasValue
                && !Request.Query.ContainsKey("departmentId")
                && !Request.Query.ContainsKey("controlId")
                && TempData["LastGroupId"] != null)
            {
                groupId = Convert.ToInt32(TempData["LastGroupId"]);
            }

            var allGroups = await _context.TestTemplateGroups
                .Include(g => g.Control)
                    .ThenInclude(c => c!.Department)
                .Where(g => g.is_active && g.control_id.HasValue && g.Control != null && g.Control.is_active)
                .OrderBy(g => g.Control!.Department != null ? g.Control.Department.SortOrder : int.MaxValue)
                .ThenBy(g => g.Control!.Department != null ? g.Control.Department.DepartmentName : "")
                .ThenBy(g => g.Control!.sort_order)
                .ThenBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToListAsync();

            if (groupId.HasValue)
            {
                var selectedGroup = allGroups.FirstOrDefault(g => g.group_id == groupId.Value);
                if (selectedGroup == null)
                {
                    groupId = null;
                }
                else
                {
                    controlId = selectedGroup.control_id;
                    selectedDepartmentId = selectedGroup.Control?.department_id;
                }
            }

            var selectedControl = controlId.HasValue
                ? allGroups.Select(g => g.Control).FirstOrDefault(c => c?.control_id == controlId.Value)
                : null;
            if (controlId.HasValue && selectedControl == null)
            {
                controlId = null;
                groupId = null;
            }
            else if (selectedControl != null)
            {
                if (!selectedDepartmentId.HasValue && !Request.Query.ContainsKey("departmentId"))
                {
                    selectedDepartmentId = selectedControl.department_id;
                }
                else if (selectedControl.department_id != selectedDepartmentId)
                {
                    controlId = null;
                    groupId = null;
                }
            }

            if (selectedDepartmentId.HasValue)
                HttpContext.Session.SetString(IndexDepartmentFilterKey, selectedDepartmentId.Value.ToString());

            var groups = selectedDepartmentId.HasValue
                ? allGroups.Where(g => g.Control?.department_id == selectedDepartmentId.Value).ToList()
                : new List<TestTemplateGroup>();

            var query = _context.TestScenarioTemplates
                .Include(x => x.Group)
                    .ThenInclude(g => g!.Control)
                        .ThenInclude(c => c!.Department)
                .AsQueryable();

            if (groupId.HasValue)
            {
                query = query.Where(x => x.group_id == groupId);
            }
            else if (controlId.HasValue)
            {
                query = query.Where(x => x.Group != null && x.Group.control_id == controlId);
            }
            else if (selectedDepartmentId.HasValue)
            {
                query = query.Where(x => x.Group != null
                    && x.Group.Control != null
                    && x.Group.Control.department_id == selectedDepartmentId.Value);
            }

            var templates = await query
                .OrderBy(x => x.template_id)
                .ToListAsync();

            ViewBag.Departments = departments;
            ViewBag.Groups = groups;
            ViewBag.Controls = groups
                .Where(g => g.Control != null)
                .Select(g => g.Control!)
                .DistinctBy(c => c.control_id)
                .ToList();
            ViewBag.SelectedDepartmentId = selectedDepartmentId;
            ViewBag.SelectedControlId = controlId;
            ViewBag.SelectedGroupId = groupId;
            ViewBag.GroupId = groupId;
            return View(templates);
        }

        // =========================
        // CREATE (GET)
        // =========================
        [RequireMenu("TestScenarioTemplates.Create")]
        public async Task<IActionResult> Create(int? groupId)
        {
            var groups = await _context.TestTemplateGroups
                .Include(g => g.Control)
                .Where(g => g.is_active && g.control_id.HasValue && g.Control != null && g.Control.is_active)
                .OrderBy(g => g.Control!.sort_order)
                .ThenBy(g => g.sort_order)
                .ThenBy(g => g.group_name)
                .ToListAsync();

            var selectedGroup = groupId.HasValue
                ? groups.FirstOrDefault(g => g.group_id == groupId.Value)
                : null;

            groupId = selectedGroup?.group_id;
            ViewBag.Groups = groups;
            ViewBag.Controls = groups
                .Where(g => g.Control != null)
                .Select(g => g.Control!)
                .DistinctBy(c => c.control_id)
                .ToList();
            ViewBag.SelectedControlId = selectedGroup?.control_id;

            // If coming from Index with selected group, preselect and lock it.
            ViewBag.LockGroup = selectedGroup != null;

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
        [RequireMenu("TestScenarioTemplates.Create")]
        public async Task<IActionResult> Create(TestScenarioTemplate model, int? controlId)
        {
            var selectedGroup = model.group_id.HasValue
                ? await _context.TestTemplateGroups
                    .Include(g => g.Control)
                    .FirstOrDefaultAsync(g => g.group_id == model.group_id.Value
                        && g.is_active
                        && g.Control != null
                        && g.Control.is_active)
                : null;

            if (!controlId.HasValue || controlId.Value <= 0)
            {
                ModelState.AddModelError("controlId", "กรุณาเลือก Template Groups Control");
            }

            if (!model.group_id.HasValue || model.group_id.Value <= 0)
            {
                ModelState.AddModelError("group_id", "กรุณาเลือก Template Group");
            }
            else if (selectedGroup == null || selectedGroup.control_id != controlId)
            {
                ModelState.AddModelError("group_id", "Template Group ไม่อยู่ภายใต้ Control ที่เลือก");
            }

            if (!ModelState.IsValid)
            {
                var groups = await _context.TestTemplateGroups
                    .Include(g => g.Control)
                    .Where(g => g.is_active && g.control_id.HasValue && g.Control != null && g.Control.is_active)
                    .OrderBy(g => g.Control!.sort_order)
                    .ThenBy(g => g.sort_order)
                    .ThenBy(g => g.group_name)
                    .ToListAsync();

                ViewBag.Groups = groups;
                ViewBag.Controls = groups
                    .Where(g => g.Control != null)
                    .Select(g => g.Control!)
                    .DistinctBy(c => c.control_id)
                    .ToList();
                ViewBag.SelectedControlId = controlId;
                ViewBag.LockGroup = false;

                return View(model);
            }

            model.created_at = DateTime.Now;
            model.updated_at = DateTime.Now;
            model.is_active = true;
            model.status_default = TestScenarioDisplay.NormalizeStatus(model.status_default);
            model.remark = string.IsNullOrWhiteSpace(model.remark) ? null : model.remark.Trim();

            _context.TestScenarioTemplates.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { groupId = model.group_id });
        }

        // =========================
        // EDIT (GET)
        // =========================
        [RequireMenu("TestScenarioTemplates.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var template = await _context.TestScenarioTemplates
                .FirstOrDefaultAsync(x => x.template_id == id);

            if (template == null)
                return NotFound();

            ViewBag.Groups = await _context.TestTemplateGroups
                .Include(g => g.Control)
                .Where(g => g.is_active)
                .OrderBy(g => g.Control != null ? g.Control.sort_order : int.MaxValue)
                .ThenBy(g => g.sort_order)
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
        [RequireMenu("TestScenarioTemplates.Edit")]
        public async Task<IActionResult> Edit(TestScenarioTemplate model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Groups = await _context.TestTemplateGroups
                    .Include(g => g.Control)
                    .Where(g => g.is_active)
                    .OrderBy(g => g.Control != null ? g.Control.sort_order : int.MaxValue)
                    .ThenBy(g => g.sort_order)
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
            template.remark = string.IsNullOrWhiteSpace(model.remark) ? null : model.remark.Trim();
            template.priority_default = model.priority_default;
            template.status_default = TestScenarioDisplay.NormalizeStatus(model.status_default);
            template.updated_at = DateTime.Now;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { groupId = template.group_id });
        }

        // =========================
        // DELETE
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestScenarioTemplates.Delete")]
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
        [RequireMenu("TestScenarioTemplates.Toggle")]
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

        private int? ResolveIndexDepartmentFilter(int? departmentId, IReadOnlySet<int> activeDepartmentIds)
        {
            if (!Request.Query.ContainsKey("departmentId"))
            {
                var rememberedValue = HttpContext.Session.GetString(IndexDepartmentFilterKey);
                return int.TryParse(rememberedValue, out var rememberedDepartmentId)
                    && activeDepartmentIds.Contains(rememberedDepartmentId)
                        ? rememberedDepartmentId
                        : null;
            }

            if (!departmentId.HasValue || !activeDepartmentIds.Contains(departmentId.Value))
            {
                HttpContext.Session.Remove(IndexDepartmentFilterKey);
                return null;
            }

            HttpContext.Session.SetString(IndexDepartmentFilterKey, departmentId.Value.ToString());
            return departmentId.Value;
        }
    }
}
