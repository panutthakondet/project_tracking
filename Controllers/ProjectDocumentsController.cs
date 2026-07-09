using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    [RequireMenu("ProjectDocuments.Index")]
    public class ProjectDocumentsController : Controller
    {
        private static readonly HashSet<string> AllowedDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "BRD",
            "TOR",
            "DESIGN",
            "BOOKIN",
            "BOOKOUT",
            "OTHER"
        };

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
        public async Task<IActionResult> Index(int? projectId)
        {
            var projects = await _context.Projects
                .Include(x => x.Coop)
                .OrderBy(x => x.Coop != null ? x.Coop.CoopName : "")
                .ThenBy(x => x.ProjectName)
                .ToListAsync();

            var selectedProject = projectId.HasValue
                ? projects.FirstOrDefault(x => x.ProjectId == projectId.Value)
                : null;

            var docs = selectedProject == null
                ? new List<ProjectDocument>()
                : await _context.ProjectDocuments
                    .Where(x => x.ProjectId == selectedProject.ProjectId)
                    .OrderByDescending(x => x.UploadedAt)
                    .ToListAsync();

            ViewBag.ProjectId = selectedProject?.ProjectId;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.Projects = projects;
            return View(docs);
        }

        // ============================
        // Upload document
        // ============================
        [RequireMenu("ProjectDocuments.Upload")]
        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 209715200)] // allow up to 200MB upload
        [RequestSizeLimit(209715200)]
        public async Task<IActionResult> Upload(int projectId, string documentType, IFormFile file)
        {
            if (projectId <= 0 || !await _context.Projects.AnyAsync(x => x.ProjectId == projectId))
            {
                TempData["Error"] = "กรุณาเลือกโครงการก่อนอัปโหลดเอกสาร";
                return RedirectToAction("Index");
            }

            var normalizedDocumentType = (documentType ?? "").Trim().ToUpperInvariant();
            if (!AllowedDocumentTypes.Contains(normalizedDocumentType))
            {
                TempData["Error"] = "ประเภทเอกสารไม่ถูกต้อง";
                return RedirectToAction("Index", new { projectId });
            }

            if (file == null || file.Length == 0)
                return RedirectToAction("Index", new { projectId });

            if (file.Length > 209715200) // 200MB
            {
                TempData["Error"] = "ไฟล์มีขนาดใหญ่เกิน 200MB";
                return RedirectToAction("Index", new { projectId });
            }

            var webRootPath = GetWebRootPath();
            var folder = Path.Combine(webRootPath, "uploads", "documents", projectId.ToString());
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var originalFileName = Path.GetFileName(file.FileName);
            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}";
            var filePath = Path.Combine(folder, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var dbPath = $"/uploads/documents/{projectId}/{fileName}";

            var doc = new ProjectDocument
            {
                ProjectId = projectId,
                DocumentType = normalizedDocumentType,
                FileName = string.IsNullOrWhiteSpace(originalFileName) ? fileName : originalFileName,
                FilePath = dbPath,
                UploadedBy = HttpContext.Session.GetString("Username"),
                UploadedAt = DateTime.Now
            };

            _context.ProjectDocuments.Add(doc);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId });
        }

        // ============================
        // Preview (ผ่าน Controller 🔥)
        // ============================
        [RequireMenu("ProjectDocuments.Preview")]
        public async Task<IActionResult> Preview(int id)
        {
            var doc = await _context.ProjectDocuments.FindAsync(id);
            if (doc == null)
                return NotFound();

            var fullPath = Path.Combine(GetWebRootPath(), doc.FilePath.TrimStart('/'));

            if (!System.IO.File.Exists(fullPath))
            {
                TempData["Error"] = "File not found.";
                return RedirectToAction("Index", new { projectId = doc.ProjectId });
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);

            // 🔥 detect content type
            var ext = Path.GetExtension(fullPath).ToLower();
            var contentType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };

            return File(bytes, contentType);
        }

        // ============================
        // Download
        // ============================
        [RequireMenu("ProjectDocuments.Download")]
        public async Task<IActionResult> Download(int id)
        {
            var doc = await _context.ProjectDocuments.FindAsync(id);
            if (doc == null)
                return NotFound();

            var fullPath = Path.Combine(GetWebRootPath(), doc.FilePath.TrimStart('/'));

            if (!System.IO.File.Exists(fullPath))
            {
                TempData["Error"] = "File not found on server.";
                return RedirectToAction("Index", new { projectId = doc.ProjectId });
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, "application/octet-stream", doc.FileName);
        }

        // ============================
        // Delete
        // ============================
        [RequireMenu("ProjectDocuments.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var doc = await _context.ProjectDocuments.FindAsync(id);
            if (doc == null)
                return RedirectToAction("Index");

            var fullPath = Path.Combine(GetWebRootPath(), doc.FilePath.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);

            _context.ProjectDocuments.Remove(doc);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = doc.ProjectId });
        }

        private string GetWebRootPath()
        {
            var webRootPath = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
                webRootPath = Path.Combine(_env.ContentRootPath, "wwwroot");

            Directory.CreateDirectory(webRootPath);
            return webRootPath;
        }
    }
}
