using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using System.Globalization;

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
                        (x.FileName != null && x.FileName.Contains(searchTerm))
                        || (x.Description != null && x.Description.Contains(searchTerm))
                        || (x.DocumentType != null
                            && (x.DocumentType.Contains(searchTerm)
                                || matchingTypeCodes.Contains(x.DocumentType)))
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

        public async Task<IActionResult> Notes(int empId, int? noteId)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .Include(x => x.Department)
                .Include(x => x.PositionDefinition)
                .FirstOrDefaultAsync(x => x.EmpId == empId);

            if (employee == null)
            {
                TempData["Error"] = "ไม่พบข้อมูลพนักงานที่ต้องการจดบันทึก";
                return RedirectToAction(nameof(Index));
            }

            var notes = await _context.EmployeeNotes
                .AsNoTracking()
                .Where(x => x.EmpId == empId)
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.EmployeeNoteId)
                .ToListAsync();

            var selectedNote = noteId.HasValue
                ? notes.FirstOrDefault(x => x.EmployeeNoteId == noteId.Value)
                : notes.FirstOrDefault();

            return View(new EmployeeNotesViewModel
            {
                Employee = employee,
                Notes = notes,
                SelectedNote = selectedNote
            });
        }

        [RequireMenu("EmployeeDocuments.Upload")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateNote(int empId)
        {
            if (empId <= 0 || !await _context.Employees.AnyAsync(x => x.EmpId == empId))
            {
                TempData["Error"] = "ไม่พบข้อมูลพนักงานที่ต้องการจดบันทึก";
                return RedirectToAction(nameof(Index));
            }

            var now = DateTime.Now;
            var username = CurrentUsername();
            var note = new EmployeeNote
            {
                EmpId = empId,
                Title = "บันทึกใหม่",
                Content = string.Empty,
                CreatedBy = username,
                UpdatedBy = username,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.EmployeeNotes.Add(note);
            await _context.SaveChangesAsync();

            TempData["Success"] = "เพิ่มบันทึกใหม่แล้ว สามารถเริ่มพิมพ์ได้ทันที";
            return RedirectToAction(nameof(Notes), new { empId, noteId = note.EmployeeNoteId });
        }

        [RequireMenu("EmployeeDocuments.Upload")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveNote(int empId, int employeeNoteId, string? title, string? content)
        {
            var note = await _context.EmployeeNotes
                .FirstOrDefaultAsync(x => x.EmployeeNoteId == employeeNoteId && x.EmpId == empId);

            if (note == null)
            {
                TempData["Error"] = "ไม่พบบันทึกที่ต้องการแก้ไข";
                return RedirectToAction(nameof(Notes), new { empId });
            }

            note.Title = LimitTextElements(
                string.IsNullOrWhiteSpace(title) ? "ไม่มีชื่อ" : title.Trim(),
                255);
            note.Content = content ?? string.Empty;
            note.UpdatedBy = CurrentUsername();
            note.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            TempData["Success"] = "บันทึกข้อความเรียบร้อย";
            return RedirectToAction(nameof(Notes), new { empId, noteId = employeeNoteId });
        }

        [RequireMenu("EmployeeDocuments.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteNote(int empId, int employeeNoteId)
        {
            var note = await _context.EmployeeNotes
                .FirstOrDefaultAsync(x => x.EmployeeNoteId == employeeNoteId && x.EmpId == empId);

            if (note == null)
            {
                TempData["Error"] = "ไม่พบบันทึกที่ต้องการลบ";
                return RedirectToAction(nameof(Notes), new { empId });
            }

            _context.EmployeeNotes.Remove(note);
            await _context.SaveChangesAsync();
            TempData["Success"] = "ลบบันทึกเรียบร้อย";
            return RedirectToAction(nameof(Notes), new { empId });
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
            return View(await GetActiveDocumentTypesAsync());
        }

        [RequireMenu("EmployeeDocuments.Upload")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadSize)]
        [RequestSizeLimit(MaxUploadSize)]
        public async Task<IActionResult> Upload(int empId, string documentType, IFormFile file, string? description)
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

            if (description?.Length > 2000)
            {
                TempData["Error"] = "คำอธิบายต้องมีความยาวไม่เกิน 2,000 ตัวอักษร";
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
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
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

        private string CurrentUsername() =>
            HttpContext.Session.GetString("Username")
            ?? User.Identity?.Name
            ?? "SYSTEM";

        private static string LimitTextElements(string value, int maximumLength)
        {
            var info = new StringInfo(value);
            return info.LengthInTextElements <= maximumLength
                ? value
                : info.SubstringByTextElements(0, maximumLength);
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
