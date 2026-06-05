using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    [RequireMenu("RequirementBoard.Index")]
    public class RequirementBoardController : BaseController
    {
        private const long MaxUploadSize = 209715200;

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public RequirementBoardController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            await EnsureDefaultColumnsAsync();

            var columns = await _context.RequirementBoardColumns
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.ColumnId)
                .ToListAsync();

            var columnIds = columns.Select(x => x.ColumnId).ToList();
            var cards = await _context.RequirementCards
                .AsNoTracking()
                .Where(x => columnIds.Contains(x.ColumnId) && !x.IsArchived)
                .Include(x => x.Attachments)
                .Include(x => x.CreatedByUser)
                .Include(x => x.CreatedByEmployee)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CardId)
                .ToListAsync();

            foreach (var column in columns)
            {
                column.Cards = cards
                    .Where(x => x.ColumnId == column.ColumnId)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.CardId)
                    .ToList();
            }

            var onlineCutoff = DateTime.Now.AddMinutes(-5);
            var onlineRows = await (
                from user in _context.LoginUsers.AsNoTracking()
                join employee in _context.Employees.AsNoTracking()
                    on user.UserId equals employee.LoginUserId into employeeJoin
                from employee in employeeJoin.DefaultIfEmpty()
                where user.Status == "ACTIVE"
                      && user.LastSeenAt.HasValue
                      && user.LastSeenAt.Value >= onlineCutoff
                orderby user.LastSeenAt descending
                select new
                {
                    user.UserId,
                    user.Username,
                    user.ProfileImagePath,
                    user.LastSeenAt,
                    EmployeeName = employee != null ? employee.EmpName : null
                })
                .ToListAsync();

            var onlineUsers = onlineRows
                .GroupBy(x => x.UserId)
                .Select((group, index) =>
                {
                    var row = group.First();
                    var displayName = !string.IsNullOrWhiteSpace(row.EmployeeName)
                        ? row.EmployeeName!
                        : row.Username;

                    return new RequirementBoardOnlineUserViewModel
                    {
                        UserId = row.UserId,
                        DisplayName = displayName,
                        AvatarPath = ResolveOnlineAvatarPath(row.ProfileImagePath),
                        ColorClass = $"c{(index % 5) + 1}",
                        LastSeenAt = row.LastSeenAt
                    };
                })
                .ToList();

            var model = new RequirementBoardViewModel
            {
                Columns = columns,
                OnlineUsers = onlineUsers,
                TotalCards = cards.Count,
                TotalAttachments = cards.Sum(x => x.Attachments.Count)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateColumn(string columnName)
        {
            columnName = (columnName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(columnName))
            {
                TempData["Error"] = "กรุณากรอกชื่อหัวข้อ";
                return RedirectToAction(nameof(Index));
            }

            var maxSort = await _context.RequirementBoardColumns
                .Select(x => (int?)x.SortOrder)
                .MaxAsync() ?? 0;

            _context.RequirementBoardColumns.Add(new RequirementBoardColumn
            {
                ColumnName = columnName,
                SortOrder = maxSort + 1,
                IsActive = true,
                CreatedByUserId = CurrentUserId(),
                CreatedByEmpId = CurrentEmpId(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadSize)]
        [RequestSizeLimit(MaxUploadSize)]
        public async Task<IActionResult> CreateCard(int columnId, string title, string? detail, IFormFile? coverImage, List<IFormFile>? files)
        {
            title = (title ?? "").Trim();
            if (columnId <= 0 || !await _context.RequirementBoardColumns.AnyAsync(x => x.ColumnId == columnId && x.IsActive))
            {
                TempData["Error"] = "ไม่พบหัวข้อสำหรับเพิ่มการ์ด";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "กรุณากรอกหัวข้อการ์ด";
                return RedirectToAction(nameof(Index));
            }

            if (!IsValidCoverImage(coverImage))
            {
                TempData["Error"] = "รูปพื้นหลังต้องเป็นไฟล์รูปภาพเท่านั้น";
                return RedirectToAction(nameof(Index));
            }

            var existingCards = await _context.RequirementCards
                .Where(x => x.ColumnId == columnId && !x.IsArchived)
                .ToListAsync();

            foreach (var existingCard in existingCards)
            {
                existingCard.SortOrder += 1;
            }

            var card = new RequirementCard
            {
                ColumnId = columnId,
                Title = title,
                Detail = detail,
                SortOrder = 1,
                CreatedByUserId = CurrentUserId(),
                CreatedByEmpId = CurrentEmpId(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.RequirementCards.Add(card);
            await _context.SaveChangesAsync();

            var cover = await SaveCoverImageAsync(card.CardId, coverImage, null);
            if (cover != null)
            {
                card.CoverImagePath = cover.Value.Path;
                card.CoverImageName = cover.Value.Name;
                card.UpdatedAt = DateTime.Now;
            }

            await SaveAttachmentsAsync(card.CardId, files);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadSize)]
        [RequestSizeLimit(MaxUploadSize)]
        public async Task<IActionResult> UpdateCard(int cardId, string title, string? detail, IFormFile? coverImage, List<IFormFile>? files)
        {
            var card = await _context.RequirementCards.FirstOrDefaultAsync(x => x.CardId == cardId && !x.IsArchived);
            if (card == null) return NotFound();

            title = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "กรุณากรอกหัวข้อการ์ด";
                return RedirectToAction(nameof(Index));
            }

            card.Title = title;
            card.Detail = detail;
            card.UpdatedAt = DateTime.Now;

            if (!IsValidCoverImage(coverImage))
            {
                TempData["Error"] = "รูปพื้นหลังต้องเป็นไฟล์รูปภาพเท่านั้น";
                return RedirectToAction(nameof(Index));
            }

            var cover = await SaveCoverImageAsync(card.CardId, coverImage, card.CoverImagePath);
            if (cover != null)
            {
                card.CoverImagePath = cover.Value.Path;
                card.CoverImageName = cover.Value.Name;
                card.UpdatedAt = DateTime.Now;
            }

            await SaveAttachmentsAsync(card.CardId, files);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveCard(int cardId)
        {
            var card = await _context.RequirementCards.FirstOrDefaultAsync(x => x.CardId == cardId);
            if (card == null) return NotFound();

            card.IsArchived = true;
            card.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAttachment(int attachmentId)
        {
            var attachment = await _context.RequirementCardAttachments
                .FirstOrDefaultAsync(x => x.AttachmentId == attachmentId);
            if (attachment == null) return NotFound();

            DeletePhysicalFile(attachment.FilePath);
            _context.RequirementCardAttachments.Remove(attachment);

            var card = await _context.RequirementCards.FirstOrDefaultAsync(x => x.CardId == attachment.CardId);
            if (card != null) card.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAttachment(int id)
        {
            var attachment = await _context.RequirementCardAttachments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.AttachmentId == id);
            if (attachment == null) return NotFound();

            var fullPath = ToFullPath(attachment.FilePath);
            if (!System.IO.File.Exists(fullPath)) return NotFound();

            var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
                ? "application/octet-stream"
                : attachment.ContentType;

            return PhysicalFile(fullPath, contentType, attachment.FileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveCard([FromBody] MoveRequirementCardRequest request)
        {
            if (request == null || request.CardId <= 0 || request.ColumnId <= 0)
                return BadRequest(new { ok = false, message = "ข้อมูลไม่ครบ" });

            var targetColumnExists = await _context.RequirementBoardColumns
                .AnyAsync(x => x.ColumnId == request.ColumnId && x.IsActive);
            if (!targetColumnExists)
                return BadRequest(new { ok = false, message = "ไม่พบหัวข้อปลายทาง" });

            var card = await _context.RequirementCards.FirstOrDefaultAsync(x => x.CardId == request.CardId && !x.IsArchived);
            if (card == null) return NotFound(new { ok = false, message = "ไม่พบการ์ด" });

            card.ColumnId = request.ColumnId;
            card.UpdatedAt = DateTime.Now;

            var orderedIds = request.OrderedCardIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (orderedIds.Count > 0)
            {
                var newSortOrder = orderedIds.IndexOf(card.CardId);
                if (newSortOrder >= 0)
                    card.SortOrder = newSortOrder + 1;

                var otherOrderedIds = orderedIds
                    .Where(x => x != card.CardId)
                    .ToList();

                var cards = await _context.RequirementCards
                    .Where(x => otherOrderedIds.Contains(x.CardId) && !x.IsArchived)
                    .ToListAsync();

                for (var i = 0; i < orderedIds.Count; i++)
                {
                    if (orderedIds[i] == card.CardId) continue;

                    var item = cards.FirstOrDefault(x => x.CardId == orderedIds[i]);
                    if (item != null)
                    {
                        item.ColumnId = request.ColumnId;
                        item.SortOrder = i + 1;
                        item.UpdatedAt = DateTime.Now;
                    }
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        private async Task EnsureDefaultColumnsAsync()
        {
            if (await _context.RequirementBoardColumns.AnyAsync()) return;

            var now = DateTime.Now;
            var userId = CurrentUserId();
            var empId = CurrentEmpId();

            _context.RequirementBoardColumns.AddRange(
                new RequirementBoardColumn
                {
                    ColumnName = "To Do",
                    SortOrder = 1,
                    IsActive = true,
                    CreatedByUserId = userId,
                    CreatedByEmpId = empId,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new RequirementBoardColumn
                {
                    ColumnName = "Complete",
                    SortOrder = 2,
                    IsActive = true,
                    CreatedByUserId = userId,
                    CreatedByEmpId = empId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

            await _context.SaveChangesAsync();
        }

        private async Task SaveAttachmentsAsync(int cardId, List<IFormFile>? files)
        {
            if (files == null || files.Count == 0) return;

            var folder = Path.Combine(_env.WebRootPath, "uploads", "requirement-cards", cardId.ToString());
            Directory.CreateDirectory(folder);

            foreach (var file in files.Where(x => x != null && x.Length > 0))
            {
                if (file.Length > MaxUploadSize)
                    throw new InvalidOperationException("ไฟล์มีขนาดใหญ่เกิน 200MB");

                var originalName = Path.GetFileName(file.FileName);
                var extension = Path.GetExtension(originalName);
                var storedName = $"{Guid.NewGuid():N}{extension}";
                var fullPath = Path.Combine(folder, storedName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _context.RequirementCardAttachments.Add(new RequirementCardAttachment
                {
                    CardId = cardId,
                    FileName = originalName,
                    StoredFileName = storedName,
                    FilePath = $"/uploads/requirement-cards/{cardId}/{storedName}",
                    ContentType = file.ContentType,
                    FileSize = file.Length,
                    UploadedByUserId = CurrentUserId(),
                    UploadedByEmpId = CurrentEmpId(),
                    UploadedAt = DateTime.Now
                });
            }
        }

        private static bool IsValidCoverImage(IFormFile? file)
        {
            if (file == null || file.Length == 0) return true;
            return (file.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveOnlineAvatarPath(string? profileImagePath)
        {
            if (string.IsNullOrWhiteSpace(profileImagePath)) return "/images/Profile/profile.png";

            var path = profileImagePath.Trim();
            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private async Task<(string Path, string Name)?> SaveCoverImageAsync(int cardId, IFormFile? file, string? oldPath)
        {
            if (file == null || file.Length == 0) return null;

            var folder = Path.Combine(_env.WebRootPath, "uploads", "requirement-cards", cardId.ToString(), "cover");
            Directory.CreateDirectory(folder);

            var originalName = Path.GetFileName(file.FileName);
            var extension = Path.GetExtension(originalName);
            var storedName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(folder, storedName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            DeletePhysicalFile(oldPath);
            return ($"/uploads/requirement-cards/{cardId}/cover/{storedName}", originalName);
        }

        private void DeletePhysicalFile(string? dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return;

            var fullPath = ToFullPath(dbPath);
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        private string ToFullPath(string dbPath)
        {
            return Path.Combine(_env.WebRootPath, dbPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }

        private int? CurrentUserId() => HttpContext.Session.GetInt32("UserId");

        private int? CurrentEmpId()
        {
            var empId = HttpContext.Session.GetInt32("EmpId");
            if (empId != null) return empId;
            return null;
        }
    }
}
