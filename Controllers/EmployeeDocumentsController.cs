using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    [RequireMenu("EmployeeDocuments.Index")]
    public class EmployeeDocumentsController : Controller
    {
        private const long MaxUploadSize = 209715200; // 200 MB
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public EmployeeDocumentsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public async Task<IActionResult> Index(int? empId, string? q)
        {
            var employees = await _context.Employees
                .Include(x => x.Department)
                .Include(x => x.PositionDefinition)
                .OrderBy(x => x.EmpName)
                .ToListAsync();

            var selectedEmployee = empId.HasValue
                ? employees.FirstOrDefault(x => x.EmpId == empId.Value)
                : null;

            var documentTypes = await GetActiveDocumentTypesAsync();
            var searchTerm = (q ?? string.Empty).Trim();
            var documents = new List<EmployeeDocument>();

            if (selectedEmployee != null)
            {
                var documentQuery = _context.EmployeeDocuments
                    .AsNoTracking()
                    .Where(x => x.EmpId == selectedEmployee.EmpId);

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var matchingTypeCodes = documentTypes
                        .Where(x => x.DocumentTypeCode.Contains(searchTerm)
                            || x.DocumentTypeName.Contains(searchTerm))
                        .Select(x => x.DocumentTypeCode.Trim().ToUpper())
                        .ToList();

                    documentQuery = documentQuery.Where(x =>
                        x.FileName.Contains(searchTerm)
                        || x.DocumentType.Contains(searchTerm)
                        || matchingTypeCodes.Contains(x.DocumentType)
                        || (x.UploadedBy != null && x.UploadedBy.Contains(searchTerm)));
                }

                documents = await documentQuery
                    .OrderByDescending(x => x.UploadedAt)
                    .ToListAsync();
            }

            ViewBag.EmpId = selectedEmployee?.EmpId;
            ViewBag.SelectedEmployee = selectedEmployee;
            ViewBag.Employees = employees;
            ViewBag.DocumentTypes = documentTypes;
            ViewBag.SearchTerm = searchTerm;
            return View(documents);
        }

        [RequireMenu("EmployeeDocuments.Upload")]
        [HttpGet]
        public async Task<IActionResult> Upload(int empId)
        {
            if (empId <= 0 || !await _context.Employees.AnyAsync(x => x.EmpId == empId))
            {
                TempData["Error"] = "กรุณาเลือกพนักงานก่อนอัปโหลดเอกสาร";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.EmpId = empId;
            ViewBag.DocumentTypes = await GetActiveDocumentTypesAsync();
            return View();
        }

        [RequireMenu("EmployeeDocuments.Upload")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadSize)]
        [RequestSizeLimit(MaxUploadSize)]
        public async Task<IActionResult> Upload(int empId, string documentType, IFormFile file)
        {
            if (empId <= 0 || !await _context.Employees.AnyAsync(x => x.EmpId == empId))
            {
                TempData["Error"] = "กรุณาเลือกพนักงานก่อนอัปโหลดเอกสาร";
                return RedirectToAction(nameof(Index));
            }

            var normalizedDocumentType = (documentType ?? string.Empty).Trim().ToUpperInvariant();
            var allowedDocumentType = (await GetActiveDocumentTypesAsync())
                .FirstOrDefault(x => string.Equals(
                    x.DocumentTypeCode.Trim(),
                    normalizedDocumentType,
                    StringComparison.OrdinalIgnoreCase));

            if (allowedDocumentType == null)
            {
                TempData["Error"] = "ประเภทเอกสารไม่ถูกต้อง หรือถูกปิดใช้งานแล้ว";
                return RedirectToAction(nameof(Index), new { empId });
            }

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "กรุณาเลือกไฟล์ที่ต้องการอัปโหลด";
                return RedirectToAction(nameof(Index), new { empId });
            }

            if (file.Length > MaxUploadSize)
            {
                TempData["Error"] = "ไฟล์มีขนาดใหญ่เกิน 200MB";
                return RedirectToAction(nameof(Index), new { empId });
            }

            var webRootPath = GetWebRootPath();
            var folder = Path.Combine(webRootPath, "uploads", "employee-documents", empId.ToString());
            Directory.CreateDirectory(folder);

            var originalFileName = Path.GetFileName(file.FileName);
            var savedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}";
            var fullPath = Path.Combine(folder, savedFileName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            _context.EmployeeDocuments.Add(new EmployeeDocument
            {
                EmpId = empId,
                DocumentType = allowedDocumentType.DocumentTypeCode.Trim().ToUpperInvariant(),
                FileName = string.IsNullOrWhiteSpace(originalFileName) ? savedFileName : originalFileName,
                FilePath = $"/uploads/employee-documents/{empId}/{savedFileName}",
                UploadedBy = HttpContext.Session.GetString("Username"),
                UploadedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = "อัปโหลดเอกสารพนักงานเรียบร้อย";
            return RedirectToAction(nameof(Index), new { empId });
        }

        [RequireMenu("EmployeeDocuments.Preview")]
        public async Task<IActionResult> Preview(int id)
        {
            var document = await _context.EmployeeDocuments.FindAsync(id);
            if (document == null) return NotFound();

            var fullPath = Path.Combine(GetWebRootPath(), document.FilePath.TrimStart('/'));
            if (!System.IO.File.Exists(fullPath))
            {
                TempData["Error"] = "ไม่พบไฟล์บนเซิร์ฟเวอร์";
                return RedirectToAction(nameof(Index), new { empId = document.EmpId });
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, GetContentType(fullPath));
        }

        [RequireMenu("EmployeeDocuments.Download")]
        public async Task<IActionResult> Download(int id)
        {
            var document = await _context.EmployeeDocuments.FindAsync(id);
            if (document == null) return NotFound();

            var fullPath = Path.Combine(GetWebRootPath(), document.FilePath.TrimStart('/'));
            if (!System.IO.File.Exists(fullPath))
            {
                TempData["Error"] = "ไม่พบไฟล์บนเซิร์ฟเวอร์";
                return RedirectToAction(nameof(Index), new { empId = document.EmpId });
            }

            return File(await System.IO.File.ReadAllBytesAsync(fullPath), "application/octet-stream", document.FileName);
        }

        [RequireMenu("EmployeeDocuments.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var document = await _context.EmployeeDocuments.FindAsync(id);
            if (document == null) return RedirectToAction(nameof(Index));

            var fullPath = Path.Combine(GetWebRootPath(), document.FilePath.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);

            _context.EmployeeDocuments.Remove(document);
            await _context.SaveChangesAsync();
            TempData["Success"] = "ลบเอกสารพนักงานเรียบร้อย";
            return RedirectToAction(nameof(Index), new { empId = document.EmpId });
        }

        private Task<List<EmployeeDocumentType>> GetActiveDocumentTypesAsync() =>
            _context.EmployeeDocumentTypes
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DocumentTypeName)
                .ToListAsync();

        private string GetWebRootPath()
        {
            var webRootPath = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
                webRootPath = Path.Combine(_env.ContentRootPath, "wwwroot");

            Directory.CreateDirectory(webRootPath);
            return webRootPath;
        }

        private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }
}
