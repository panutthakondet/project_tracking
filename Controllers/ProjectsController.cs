using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class ProjectsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProjectsController(
            AppDbContext context,
            IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ===========================
        // LIST
        // ===========================
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> Index()
        {
            var projects = await _context.Projects
                .AsNoTracking()
                .OrderByDescending(p => p.EndDate ?? DateTime.MinValue)
                .ThenByDescending(p => p.ProjectId)
                .ToListAsync();

            return View(projects);
        }

        // ===========================
        // VIEW ONLY (Standalone page)
        // ===========================
        [HttpGet]
        [RequireMenu("Projects.ViewOnly")]
        public async Task<IActionResult> ViewOnly()
        {
            var projects = await _context.Projects
                .AsNoTracking()
                .OrderByDescending(p => p.EndDate ?? DateTime.MinValue)
                .ThenByDescending(p => p.ProjectId)
                .ToListAsync();

            return View("ViewOnly", projects);
        }

        // ===========================
        // CREATE (GET)
        // ===========================
        [RequireMenu("Projects.Index")]
        public IActionResult Create()
        {
            return View(new Project());
        }

        // ===========================
        // CREATE (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> Create(
            Project project
        )
        {
            if (!ModelState.IsValid)
            {
                return View(project);
            }

            // 1️⃣ Save Project ก่อน
            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ===========================
        // EDIT (GET)
        // ===========================
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> Edit(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null)
            {
                return NotFound();
            }

            return View(project);
        }

        // ===========================
        // EDIT (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> Edit(
            int id,
            Project model
        )
        {
            if (id != model.ProjectId)
            {
                return NotFound();
            }

            var db = await _context.Projects.FindAsync(id);
            if (db == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // ===============================
            // ✅ UPDATE FIELD (ครบทุกช่อง)
            // ===============================
            db.ProjectName = model.ProjectName;
            db.StartDate = model.StartDate;
            db.EndDate = model.EndDate;
            db.Status = model.Status;

            // 🔹 SYSTEM / DATABASE INFO
            db.LinkName = model.LinkName;
            db.DatabaseName = model.DatabaseName;
            db.TestAccount = model.TestAccount;
            db.RemoteUrl = model.RemoteUrl;

            // 🔹 DESIGN
            db.FigmaLink = model.FigmaLink;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // ===========================
        // DELETE (GET)
        // ===========================
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> Delete(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null)
            {
                return NotFound();
            }

            return View(project);
        }

        // ===========================
        // DELETE (POST)
        // ===========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project != null)
            {
                _context.Projects.Remove(project);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}