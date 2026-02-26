using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

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
        public async Task<IActionResult> Index()
        {
            var groups = await _context.TestTemplateGroups
                .OrderByDescending(x => x.created_at)
                .ToListAsync();

            return View(groups);
        }

        [RequireMenu("TestTemplateGroups.Index")]
        public IActionResult Create(int? groupId)
        {
            ViewBag.SelectedGroup = groupId;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroups.Index")]
        public async Task<IActionResult> Create(TestTemplateGroup model, int? groupId)
        {
            if (!ModelState.IsValid)
                return View(model);

            model.created_at = DateTime.Now;
            model.is_active = true;

            _context.TestTemplateGroups.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { groupId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TestTemplateGroups.Index")]
        public async Task<IActionResult> Delete(int id)
        {
            var group = await _context.TestTemplateGroups.FindAsync(id);
            if (group == null)
                return NotFound();

            _context.TestTemplateGroups.Remove(group);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}