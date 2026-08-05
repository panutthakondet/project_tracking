using ProjectTracking.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class TestTemplateGroupsController : BaseController
    {
        private readonly AppDbContext _context;

        public TestTemplateGroupsController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("TestTemplateGroups.Index")]
        public async Task<IActionResult> Index(int? controlId)
        {
            var query = _context.TestTemplateGroups
                .AsNoTracking()
                .Include(x => x.Control)
                .AsQueryable();
            if (controlId.HasValue)
                query = query.Where(x => x.control_id == controlId.Value);

            var groups = await query
                .OrderBy(x => x.Control != null ? x.Control.sort_order : int.MaxValue)
                .ThenBy(x => x.sort_order)
                .ThenByDescending(x => x.created_at)
                .ToListAsync();

            await LoadControlsAsync(controlId);

            return View(groups);
        }

        [RequireMenu("TestTemplateGroups.Create")]
        public async Task<IActionResult> Create(int? controlId)
        {
            await LoadControlsAsync(controlId);
            return View(new TestTemplateGroup { control_id = controlId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroups.Create")]
        public async Task<IActionResult> Create(TestTemplateGroup model)
        {
            await ValidateGroupAsync(model);
            if (!ModelState.IsValid)
            {
                await LoadControlsAsync(model.control_id);
                return View(model);
            }

            model.group_name = model.group_name.Trim();
            model.created_at = DateTime.Now;
            model.is_active = true;

            _context.TestTemplateGroups.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { controlId = model.control_id });
        }

        [RequireMenu("TestTemplateGroups.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var group = await _context.TestTemplateGroups.FindAsync(id);
            if (group == null)
                return NotFound();

            await LoadControlsAsync(group.control_id);

            return View(group);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroups.Edit")]
        public async Task<IActionResult> Edit(int id, TestTemplateGroup model)
        {
            if (id != model.group_id)
                return NotFound();

            await ValidateGroupAsync(model, id);
            if (!ModelState.IsValid)
            {
                await LoadControlsAsync(model.control_id);
                return View(model);
            }

            var existing = await _context.TestTemplateGroups.AsNoTracking()
                .FirstOrDefaultAsync(x => x.group_id == id);
            if (existing == null)
                return NotFound();

            // Preserve created_at
            model.created_at = existing.created_at;
            model.group_name = model.group_name.Trim();

            _context.TestTemplateGroups.Update(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroups.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var group = await _context.TestTemplateGroups.FindAsync(id);
            if (group == null)
                return NotFound();

            _context.TestTemplateGroups.Remove(group);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
        [HttpPost]
        [RequireMenu("TestTemplateGroups.Sort")]
        public async Task<IActionResult> UpdateSort([FromBody] List<SortDto> data)
        {
            if (data == null || data.Count == 0)
                return BadRequest();

            foreach (var item in data)
            {
                var group = await _context.TestTemplateGroups.FindAsync(item.id);
                if (group != null)
                {
                    group.sort_order = item.sort;
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

        private async Task LoadControlsAsync(int? selectedControl)
        {
            ViewBag.Controls = await _context.TestTemplateGroupControls
                .AsNoTracking()
                .Where(x => x.is_active)
                .OrderBy(x => x.sort_order)
                .ThenBy(x => x.control_name)
                .ToListAsync();
            ViewBag.SelectedControl = selectedControl;
        }

        private async Task ValidateGroupAsync(TestTemplateGroup model, int? exceptId = null)
        {
            model.group_name = (model.group_name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model.group_name))
                ModelState.AddModelError(nameof(model.group_name), "กรุณาระบุชื่อ Group");

            if (model.control_id.HasValue && !await _context.TestTemplateGroupControls
                    .AnyAsync(x => x.control_id == model.control_id.Value && x.is_active))
                ModelState.AddModelError(nameof(model.control_id), "ไม่พบ Control ที่เลือก");

            var normalized = model.group_name.ToUpper();
            var duplicate = await _context.TestTemplateGroups.AnyAsync(x =>
                (!exceptId.HasValue || x.group_id != exceptId.Value)
                && x.control_id == model.control_id
                && x.group_name.Trim().ToUpper() == normalized);
            if (duplicate)
                ModelState.AddModelError(nameof(model.group_name), "ชื่อ Group นี้มีอยู่แล้วภายใน Control ที่เลือก");
        }
    }
}
