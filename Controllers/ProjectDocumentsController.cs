using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class ProjectDocumentsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProjectDocumentsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ============================
        // List documents by project
        // ============================
        public async Task<IActionResult> Index(int projectId)
        {
            var docs = await _context.ProjectDocuments
                .Where(x => x.ProjectId == projectId)
                .OrderByDescending(x => x.UploadedAt)
                .ToListAsync();

            ViewBag.ProjectId = projectId;
            return View(docs);
        }

        // ============================
        // Upload document
        // ============================
        [RequestSizeLimit(209715200)] // allow up to 200MB upload
        [HttpPost]
        public async Task<IActionResult> Upload(int projectId, string documentType, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return RedirectToAction("Index", new { projectId });

            if (file.Length > 209715200) // 200MB
            {
                TempData["Error"] = "ไฟล์มีขนาดใหญ่เกิน 200MB";
                return RedirectToAction("Index", new { projectId });
            }

            var folder = Path.Combine(_env.WebRootPath, "uploads", "documents", projectId.ToString());
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(folder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var dbPath = $"/uploads/documents/{projectId}/{fileName}";

            var doc = new ProjectDocument
            {
                ProjectId = projectId,
                DocumentType = documentType,
                FileName = file.FileName,
                FilePath = dbPath,
                UploadedAt = DateTime.Now
            };

            _context.ProjectDocuments.Add(doc);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId });
        }

        // ============================
        // Download
        // ============================
        public async Task<IActionResult> Download(int id)
        {
            var doc = await _context.ProjectDocuments.FindAsync(id);
            if (doc == null)
                return NotFound();

            var fullPath = Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/'));
            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);

            return File(bytes, "application/octet-stream", doc.FileName);
        }

        // ============================
        // Delete
        // ============================
        public async Task<IActionResult> Delete(int id)
        {
            var doc = await _context.ProjectDocuments.FindAsync(id);
            if (doc == null)
                return RedirectToAction("Index");

            var fullPath = Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);

            _context.ProjectDocuments.Remove(doc);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = doc.ProjectId });
        }
    }
}
