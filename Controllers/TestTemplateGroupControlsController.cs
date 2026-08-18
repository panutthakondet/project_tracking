using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class TestTemplateGroupControlsController : BaseController
    {
        private const string IndexDepartmentFilterKey = "TestTemplateGroupControls.Filter.DepartmentId";
        private readonly AppDbContext _context;

        public TestTemplateGroupControlsController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("TestTemplateGroupControls.Index")]
        public async Task<IActionResult> Index(int? departmentId)
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

            var controlsQuery = _context.TestTemplateGroupControls
                .AsNoTracking()
                .Include(x => x.Department)
                .Include(x => x.Groups)
                .AsQueryable();
            if (selectedDepartmentId.HasValue)
                controlsQuery = controlsQuery.Where(x => x.department_id == selectedDepartmentId.Value);

            var controls = await controlsQuery
                .OrderBy(x => x.Department != null ? x.Department.SortOrder : int.MaxValue)
                .ThenBy(x => x.Department != null ? x.Department.DepartmentName : "")
                .ThenBy(x => x.sort_order)
                .ThenBy(x => x.control_name)
                .ToListAsync();

            ViewBag.Departments = departments;
            ViewBag.SelectedDepartmentId = selectedDepartmentId;
            return View(controls);
        }

        [RequireMenu("TestTemplateGroupControls.Create")]
        public async Task<IActionResult> Create(int? departmentId)
        {
            var departments = await LoadDepartmentsAsync();
            var selectedDepartmentId = departmentId.HasValue
                && departments.Any(x => x.DepartmentId == departmentId.Value)
                    ? departmentId
                    : null;
            return View(new TestTemplateGroupControl { department_id = selectedDepartmentId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroupControls.Create")]
        public async Task<IActionResult> Create(TestTemplateGroupControl model)
        {
            model.control_name = (model.control_name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model.control_name))
                ModelState.AddModelError(nameof(model.control_name), "กรุณาระบุชื่อ Control");
            if (await ControlNameExistsAsync(model.control_name))
                ModelState.AddModelError(nameof(model.control_name), "ชื่อ Control นี้มีอยู่แล้ว");
            await ValidateDepartmentAsync(model.department_id);

            if (!ModelState.IsValid)
            {
                await LoadDepartmentsAsync();
                return View(model);
            }

            model.is_active = true;
            model.created_at = DateTime.Now;
            _context.TestTemplateGroupControls.Add(model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { departmentId = model.department_id });
        }

        [RequireMenu("TestTemplateGroupControls.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var control = await _context.TestTemplateGroupControls.FindAsync(id);
            if (control == null)
                return NotFound();

            await LoadDepartmentsAsync();
            return View(control);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroupControls.Edit")]
        public async Task<IActionResult> Edit(int id, TestTemplateGroupControl model)
        {
            if (id != model.control_id)
                return NotFound();

            var control = await _context.TestTemplateGroupControls.FindAsync(id);
            if (control == null)
                return NotFound();

            model.control_name = (model.control_name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model.control_name))
                ModelState.AddModelError(nameof(model.control_name), "กรุณาระบุชื่อ Control");
            if (await ControlNameExistsAsync(model.control_name, id))
                ModelState.AddModelError(nameof(model.control_name), "ชื่อ Control นี้มีอยู่แล้ว");
            await ValidateDepartmentAsync(model.department_id);

            if (!ModelState.IsValid)
            {
                await LoadDepartmentsAsync();
                return View(model);
            }

            control.department_id = model.department_id;
            control.control_name = model.control_name;
            control.sort_order = model.sort_order;
            control.is_active = model.is_active;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroupControls.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var control = await _context.TestTemplateGroupControls.FindAsync(id);
            if (control == null)
                return NotFound();

            _context.TestTemplateGroupControls.Remove(control);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [RequireMenu("TestTemplateGroupControls.Sort")]
        public async Task<IActionResult> UpdateSort([FromBody] List<SortDto> data)
        {
            if (data == null || data.Count == 0)
                return BadRequest();

            var ids = data.Select(x => x.id).Distinct().ToList();
            var controls = await _context.TestTemplateGroupControls
                .Where(x => ids.Contains(x.control_id))
                .ToDictionaryAsync(x => x.control_id);
            foreach (var item in data)
            {
                if (controls.TryGetValue(item.id, out var control))
                    control.sort_order = item.sort;
            }
            await _context.SaveChangesAsync();
            return Ok();
        }

        private Task<bool> ControlNameExistsAsync(string name, int? exceptId = null)
        {
            var normalized = name.Trim().ToUpper();
            return _context.TestTemplateGroupControls.AnyAsync(x =>
                (!exceptId.HasValue || x.control_id != exceptId.Value)
                && x.control_name.Trim().ToUpper() == normalized);
        }

        private async Task<List<ProjectDepartment>> LoadDepartmentsAsync()
        {
            var departments = await _context.ProjectDepartments
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DepartmentName)
                .ToListAsync();
            ViewBag.Departments = departments;
            return departments;
        }

        private async Task ValidateDepartmentAsync(int? departmentId)
        {
            if (!departmentId.HasValue || departmentId.Value <= 0)
            {
                ModelState.AddModelError(nameof(TestTemplateGroupControl.department_id), "กรุณาเลือกฝ่าย");
                return;
            }

            var exists = await _context.ProjectDepartments
                .AsNoTracking()
                .AnyAsync(x => x.DepartmentId == departmentId.Value && x.IsActive);
            if (!exists)
                ModelState.AddModelError(nameof(TestTemplateGroupControl.department_id), "ฝ่ายที่เลือกไม่พร้อมใช้งาน");
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

        public class SortDto
        {
            public int id { get; set; }
            public int sort { get; set; }
        }
    }
}
