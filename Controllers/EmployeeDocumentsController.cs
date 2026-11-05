using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using System.Globalization;
using ProjectTracking.Services;

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

            // เลือกพนักงานของผู้ล็อกอินเมื่อเปิดเมนูครั้งแรกเท่านั้น
            if (!empId.HasValue && !Request.Query.ContainsKey("empId"))
            {
                empId = HttpContext.Session.GetInt32("EmpId");
                if (!employees.Any(x => x.EmpId == empId))
                {
                    empId = null;
                    var userId = HttpContext.Session.GetInt32("UserId");
                    if (userId.HasValue)
                    {
                        var linkedEmpId = await _context.LoginUsers
                            .AsNoTracking()
                            .Where(x => x.UserId == userId.Value)
                            .Select(x => x.EmpId)
                            .FirstOrDefaultAsync();
                        empId = employees.FirstOrDefault(x => x.EmpId == linkedEmpId)?.EmpId
                            ?? employees.FirstOrDefault(x => x.LoginUserId == userId.Value)?.EmpId;
                    }
                }
            }

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

        [HttpGet]
        public async Task<IActionResult> MyNotes()
        {
            var empId = HttpContext.Session.GetInt32("EmpId");
            if (!empId.HasValue || !await _context.Employees.AnyAsync(x => x.EmpId == empId.Value))
            {
                empId = null;
                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId.HasValue)
                {
                    var linkedEmpId = await _context.LoginUsers.AsNoTracking()
                        .Where(x => x.UserId == userId.Value).Select(x => x.EmpId).FirstOrDefaultAsync();
                    empId = await _context.Employees.AsNoTracking()
                        .Where(x => x.EmpId == linkedEmpId || x.LoginUserId == userId.Value)
                        .OrderByDescending(x => x.EmpId == linkedEmpId)
                        .Select(x => (int?)x.EmpId).FirstOrDefaultAsync();
                }
            }

            if (!empId.HasValue)
            {
                TempData["Error"] = "บัญชีของคุณยังไม่ได้ผูกกับพนักงาน กรุณาเลือกพนักงานเพื่อเปิดบันทึก";
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Notes), new { empId = empId.Value });
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
                .OrderByDescending(x => x.IsPinned)
                .ThenByDescending(x => x.UpdatedAt)
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

        [RequireMenu("EmployeeDocuments.Upload")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetNotePinned(int empId, int employeeNoteId, bool isPinned)
        {
            var note = await _context.EmployeeNotes
                .FirstOrDefaultAsync(x => x.EmployeeNoteId == employeeNoteId && x.EmpId == empId);
            if (note == null)
            {
                TempData["Error"] = "ไม่พบบันทึกที่ต้องการปักหมุด";
                return RedirectToAction(nameof(Notes), new { empId });
            }

            note.IsPinned = isPinned;
            await _context.SaveChangesAsync();
            TempData["Success"] = isPinned ? "ปักหมุดบันทึกแล้ว" : "ยกเลิกปักหมุดบันทึกแล้ว";
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

            // เก็บไฟล์ทุกชนิดนอก wwwroot เพื่อไม่ให้ไฟล์งานถูกเรียกเป็นหน้าเว็บหรือสคริปต์
            var folder = Path.Combine(_env.ContentRootPath, "App_Data", "employee-documents", empId.ToString());
            Directory.CreateDirectory(folder);

            var originalFileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (originalFileName.Length > 255)
            {
                TempData["Error"] = "ชื่อไฟล์ต้องมีความยาวไม่เกิน 255 ตัวอักษร";
                return RedirectToAction(nameof(Index), new { empId });
            }
            var savedFileName = $"{Guid.NewGuid():N}.bin";
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
                FilePath = $"/App_Data/employee-documents/{empId}/{savedFileName}",
                UploadedBy = HttpContext.Session.GetString("Username"),
                UploadedAt = DateTime.Now
            });

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                System.IO.File.Delete(fullPath);
                throw;
            }
            TempData["Success"] = "อัปโหลดไฟล์พนักงานเรียบร้อย";
            return RedirectToAction(nameof(Index), new { empId });
        }

        private string UploadStagingRoot => Path.Combine(_env.ContentRootPath, "App_Data", "employee-document-staging");
        private int UploadUserId => HttpContext.Session.GetInt32("UserId") ?? 0;

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("EmployeeDocuments.Upload")]
        public async Task<IActionResult> BeginUpload(int empId, string? documentType, string? fileName, long fileSize, string? description)
        {
            if (!ModelState.IsValid || fileSize <= 0 || description?.Length > 2000)
                return BadRequest(new { message = "ข้อมูลไฟล์ไม่ถูกต้อง หรือคำอธิบายเกิน 2,000 ตัวอักษร" });
            if (!await _context.Employees.AnyAsync(x => x.EmpId == empId))
                return BadRequest(new { message = "ไม่พบพนักงานที่เลือก" });
            var type = (await GetActiveDocumentTypesAsync()).FirstOrDefault(x =>
                string.Equals(x.DocumentTypeCode.Trim(), documentType?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (type == null) return BadRequest(new { message = "ประเภทเอกสารไม่ถูกต้อง" });
            var name = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
                return BadRequest(new { message = "กรุณาระบุชื่อไฟล์ไม่เกิน 255 ตัวอักษร" });
            EmployeeFileUploadStore.RemoveExpired(UploadStagingRoot, UploadUserId);
            var uploadId = await EmployeeFileUploadStore.BeginAsync(UploadStagingRoot, UploadUserId,
                new(empId, type.DocumentTypeCode.Trim().ToUpperInvariant(), name,
                    string.IsNullOrWhiteSpace(description) ? null : description.Trim(), fileSize));
            return Json(new { uploadId, chunkSize = EmployeeFileUploadStore.ChunkSize });
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("EmployeeDocuments.Upload")]
        [RequestFormLimits(MultipartBodyLengthLimit = EmployeeFileUploadStore.ChunkSize)]
        [RequestSizeLimit(EmployeeFileUploadStore.ChunkSize + 1048576)]
        public async Task<IActionResult> UploadChunk(string uploadId, long offset, IFormFile chunk)
        {
            if (!ModelState.IsValid || chunk == null) return BadRequest(new { message = "ไม่พบชิ้นส่วนไฟล์" });
            try
            {
                await using var input = chunk.OpenReadStream();
                var nextOffset = await EmployeeFileUploadStore.AppendAsync(UploadStagingRoot, UploadUserId, uploadId, offset, input, chunk.Length);
                return Json(new { nextOffset });
            }
            catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
            catch (IOException) { return Conflict(new { message = "ไม่สามารถเขียนไฟล์ได้ กรุณาตรวจพื้นที่ดิสก์และลองอัปโหลดใหม่" }); }
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("EmployeeDocuments.Upload")]
        public async Task<IActionResult> CompleteUpload(string uploadId)
        {
            EmployeeFileUploadStore.Metadata metadata;
            try { metadata = await EmployeeFileUploadStore.ReadAsync(UploadStagingRoot, UploadUserId, uploadId); }
            catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
            catch (IOException) { return BadRequest(new { message = "ไม่พบไฟล์ที่กำลังอัปโหลด กรุณาลองใหม่" }); }
            if (!await _context.Employees.AnyAsync(x => x.EmpId == metadata.EmpId))
                return BadRequest(new { message = "ไม่พบพนักงานที่เลือก" });

            var relativePath = $"/App_Data/employee-documents/{metadata.EmpId}/{uploadId}.bin";
            var fullPath = Path.Combine(_env.ContentRootPath, relativePath.TrimStart('/'));
            try { await EmployeeFileUploadStore.MoveCompletedAsync(UploadStagingRoot, UploadUserId, uploadId, fullPath); }
            catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
            catch (IOException) { return Conflict(new { message = "ไม่สามารถประกอบไฟล์ได้ กรุณาลองอัปโหลดใหม่" }); }
            _context.EmployeeDocuments.Add(new EmployeeDocument
            {
                EmpId = metadata.EmpId, DocumentType = metadata.DocumentType, FileName = metadata.FileName,
                Description = metadata.Description, FilePath = relativePath,
                UploadedBy = CurrentUsername(), UploadedAt = DateTime.Now
            });
            try { await _context.SaveChangesAsync(); }
            catch { System.IO.File.Delete(fullPath); throw; }
            // ไฟล์สำเร็จแล้ว การล้างไฟล์ชั่วคราวไม่ควรทำให้ผู้ใช้เข้าใจว่าอัปโหลดล้มเหลว
            try { EmployeeFileUploadStore.Delete(UploadStagingRoot, UploadUserId, uploadId); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            TempData["Success"] = "อัปโหลดไฟล์พนักงานเรียบร้อย";
            return Json(new { redirectUrl = Url.Action(nameof(Index), new { empId = metadata.EmpId }) });
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("EmployeeDocuments.Upload")]
        public IActionResult CancelUpload(string uploadId)
        {
            try { EmployeeFileUploadStore.Delete(UploadStagingRoot, UploadUserId, uploadId); }
            catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
            catch (IOException) { return Conflict(new { message = "ไฟล์ชั่วคราวยังใช้งานอยู่" }); }
            return Ok();
        }

        [RequireMenu("EmployeeDocuments.Preview")]
        public async Task<IActionResult> Preview(int id)
        {
            var document = await _context.EmployeeDocuments.FindAsync(id);
            if (document == null) return NotFound();

            var fullPath = GetDocumentFullPath(document);
            if (!System.IO.File.Exists(fullPath))
            {
                TempData["Error"] = "ไม่พบไฟล์บนเซิร์ฟเวอร์";
                return RedirectToAction(nameof(Index), new { empId = document.EmpId });
            }

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            var contentType = GetContentType(document.FileName ?? fullPath);
            return contentType == "application/octet-stream"
                ? PhysicalFile(fullPath, contentType, document.FileName ?? "download", enableRangeProcessing: true)
                : PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
        }

        [RequireMenu("EmployeeDocuments.Download")]
        public async Task<IActionResult> Download(int id)
        {
            var document = await _context.EmployeeDocuments.FindAsync(id);
            if (document == null) return NotFound();

            var fullPath = GetDocumentFullPath(document);
            if (!System.IO.File.Exists(fullPath))
            {
                TempData["Error"] = "ไม่พบไฟล์บนเซิร์ฟเวอร์";
                return RedirectToAction(nameof(Index), new { empId = document.EmpId });
            }

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return PhysicalFile(fullPath, "application/octet-stream", document.FileName ?? "download", enableRangeProcessing: true);
        }

        [RequireMenu("EmployeeDocuments.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var document = await _context.EmployeeDocuments.FindAsync(id);
            if (document == null) return RedirectToAction(nameof(Index));

            var fullPath = GetDocumentFullPath(document);
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

        private string GetDocumentFullPath(EmployeeDocument document)
        {
            // รองรับไฟล์เดิมที่เก็บใน wwwroot และไฟล์ใหม่ใน App_Data
            var relativePath = document.FilePath.TrimStart('/');
            var root = relativePath.StartsWith("App_Data/employee-documents/", StringComparison.Ordinal)
                ? Path.Combine(_env.ContentRootPath, "App_Data", "employee-documents")
                : Path.Combine(GetWebRootPath(), "uploads", "employee-documents");
            var prefix = relativePath.StartsWith("App_Data/employee-documents/", StringComparison.Ordinal)
                ? "App_Data/employee-documents/" : "uploads/employee-documents/";
            if (!relativePath.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("เส้นทางไฟล์พนักงานไม่ถูกต้อง");
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath[prefix.Length..]));
            if (!fullPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("เส้นทางไฟล์อยู่นอกพื้นที่จัดเก็บ");
            return fullPath;
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
