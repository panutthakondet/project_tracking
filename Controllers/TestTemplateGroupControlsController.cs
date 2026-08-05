using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class TestTemplateGroupControlsController : BaseController
    {
        private readonly AppDbContext _context;

        public TestTemplateGroupControlsController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("TestTemplateGroupControls.Index")]
        public async Task<IActionResult> Index()
        {
            var controls = await _context.TestTemplateGroupControls
                .AsNoTracking()
                .Include(x => x.Groups)
                .OrderBy(x => x.sort_order)
                .ThenBy(x => x.control_name)
                .ToListAsync();
            return View(controls);
        }

        [RequireMenu("TestTemplateGroupControls.Create")]
        public IActionResult Create() => View(new TestTemplateGroupControl());

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

            if (!ModelState.IsValid)
                return View(model);

            model.is_active = true;
            model.created_at = DateTime.Now;
            _context.TestTemplateGroupControls.Add(model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [RequireMenu("TestTemplateGroupControls.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var control = await _context.TestTemplateGroupControls.FindAsync(id);
            return control == null ? NotFound() : View(control);
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

            if (!ModelState.IsValid)
                return View(model);

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

        public class SortDto
        {
            public int id { get; set; }
            public int sort { get; set; }
        }
    }
}
