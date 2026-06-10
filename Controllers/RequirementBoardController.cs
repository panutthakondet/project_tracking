using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
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
                .Include(x => x.PhaseItems)
                .Include(x => x.Labels)
                    .ThenInclude(x => x.Label)
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

            var labels = await _context.RequirementBoardLabels
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.LabelName)
                .ToListAsync();

            var model = new RequirementBoardViewModel
            {
                Columns = columns,
                OnlineUsers = onlineUsers,
                Labels = labels,
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
        public async Task<IActionResult> UpdateCard(
            int cardId,
            string title,
            string? detail,
            IFormFile? coverImage,
            List<IFormFile>? files,
            List<int>? labelIds,
            List<RequirementCardPhaseItemInput>? phaseItems)
        {
            var isAjax = IsAjaxRequest();
            var card = await _context.RequirementCards.FirstOrDefaultAsync(x => x.CardId == cardId && !x.IsArchived);
            if (card == null)
            {
                if (isAjax) return NotFound(new { success = false, message = "ไม่พบการ์ดนี้ หรือการ์ดถูกลบแล้ว" });
                return NotFound();
            }

            title = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                if (isAjax) return BadRequest(new { success = false, message = "กรุณากรอกหัวข้อการ์ด" });
                TempData["Error"] = "กรุณากรอกหัวข้อการ์ด";
                return RedirectToAction(nameof(Index));
            }

            card.Title = title;
            card.Detail = detail;
            card.UpdatedAt = DateTime.Now;

            if (!IsValidCoverImage(coverImage))
            {
                if (isAjax) return BadRequest(new { success = false, message = "รูปพื้นหลังต้องเป็นไฟล์รูปภาพเท่านั้น" });
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

            await ReplacePhaseItemsAsync(card.CardId, phaseItems);
            await ReplaceCardLabelsAsync(card.CardId, labelIds);
            await SaveAttachmentsAsync(card.CardId, files);
            await _context.SaveChangesAsync();

            if (isAjax)
            {
                var attachmentCount = await _context.RequirementCardAttachments
                    .CountAsync(x => x.CardId == card.CardId);

                return Json(new
                {
                    success = true,
                    message = "บันทึกการ์ดเรียบร้อย",
                    cardId = card.CardId,
                    title = card.Title,
                    detail = card.Detail ?? "",
                    coverImagePath = card.CoverImagePath ?? "",
                    coverImageName = card.CoverImageName ?? "",
                    updatedAt = card.UpdatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                    attachmentCount,
                    labels = await LoadCardLabelPayloadAsync(card.CardId)
                });
            }

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

        [HttpGet]
        public async Task<IActionResult> PreviewAttachment(int id)
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

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{attachment.FileName}\"";
            return PhysicalFile(fullPath, contentType);
        }

        [HttpGet]
        public async Task<IActionResult> CardDetails(int id)
        {
            var card = await _context.RequirementCards
                .AsNoTracking()
                .Include(x => x.Column)
                .Include(x => x.CreatedByUser)
                .Include(x => x.CreatedByEmployee)
                .Include(x => x.Attachments)
                .Include(x => x.PhaseItems)
                .Include(x => x.Labels)
                    .ThenInclude(x => x.Label)
                .FirstOrDefaultAsync(x => x.CardId == id && !x.IsArchived);

            if (card == null) return NotFound();

            var createdBy = !string.IsNullOrWhiteSpace(card.CreatedByEmployee?.EmpName)
                ? card.CreatedByEmployee.EmpName
                : (!string.IsNullOrWhiteSpace(card.CreatedByUser?.Username) ? card.CreatedByUser.Username : "ไม่ระบุผู้สร้าง");

            var attachments = card.Attachments
                .OrderByDescending(x => x.UploadedAt)
                .Select(file => new
                {
                    attachmentId = file.AttachmentId,
                    fileName = file.FileName,
                    fileSize = FormatFileSize(file.FileSize),
                    uploadedAt = file.UploadedAt.ToString("dd/MM/yyyy HH:mm"),
                    isImage = IsImageContentType(file.ContentType),
                    filePath = file.FilePath,
                    previewUrl = Url.Action(nameof(PreviewAttachment), new { id = file.AttachmentId }),
                    downloadUrl = Url.Action(nameof(DownloadAttachment), new { id = file.AttachmentId })
                })
                .ToList();

            var phaseItems = card.PhaseItems
                .OrderBy(x => x.PhaseSort == 0 ? int.MaxValue : x.PhaseSort)
                .ThenBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.ItemId)
                .Select(item => new
                {
                    itemId = item.ItemId,
                    phaseName = item.PhaseName,
                    phaseType = item.PhaseType,
                    phaseOrder = item.PhaseOrder,
                    periodOrder = item.PeriodOrder,
                    phasePeriodLabel = item.PhasePeriodLabel,
                    phaseStatus = item.PhaseStatus ?? "-",
                    planDate = FormatDateRange(item.PlanStart, item.PlanEnd),
                    periodDate = FormatDate(item.PeriodEndDate)
                })
                .ToList();

            return Json(new
            {
                cardId = card.CardId,
                title = card.Title,
                detail = card.Detail,
                columnName = card.Column?.ColumnName ?? "-",
                coverImagePath = card.CoverImagePath,
                createdBy,
                updatedAt = card.UpdatedAt.ToString("dd/MM/yyyy HH:mm"),
                labels = card.Labels
                    .Where(x => x.Label != null && x.Label.IsActive)
                    .OrderBy(x => x.Label!.SortOrder)
                    .ThenBy(x => x.Label!.LabelName)
                    .Select(x => new
                    {
                        labelId = x.LabelId,
                        labelName = x.Label!.LabelName,
                        colorHex = x.Label!.ColorHex
                    })
                    .ToList(),
                phaseItems,
                attachments
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLabel(string labelName, string colorHex)
        {
            labelName = (labelName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(labelName))
                return BadRequest(new { success = false, message = "กรุณากรอกชื่อป้าย" });

            var color = NormalizeLabelColor(colorHex);
            var maxSort = await _context.RequirementBoardLabels
                .Select(x => (int?)x.SortOrder)
                .MaxAsync() ?? 0;

            var label = new RequirementBoardLabel
            {
                LabelName = labelName,
                ColorHex = color,
                SortOrder = maxSort + 1,
                IsActive = true,
                CreatedByUserId = CurrentUserId(),
                CreatedByEmpId = CurrentEmpId(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.RequirementBoardLabels.Add(label);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                label = new
                {
                    labelId = label.LabelId,
                    labelName = label.LabelName,
                    colorHex = label.ColorHex
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCardLabels(int cardId, List<int>? labelIds)
        {
            var card = await _context.RequirementCards
                .FirstOrDefaultAsync(x => x.CardId == cardId && !x.IsArchived);

            if (card == null)
            {
                return NotFound(new { success = false, message = "ไม่พบการ์ดนี้ หรือการ์ดถูกลบแล้ว" });
            }

            card.UpdatedAt = DateTime.Now;
            await ReplaceCardLabelsAsync(card.CardId, labelIds);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                cardId = card.CardId,
                updatedAt = card.UpdatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                labels = await LoadCardLabelPayloadAsync(card.CardId)
            });
        }

        [HttpGet]
        public async Task<IActionResult> ProjectCardDetails(int projectId)
        {
            var cardId = await _context.Projects
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId)
                .Select(x => x.RequirementCardId)
                .FirstOrDefaultAsync();

            if (!cardId.HasValue) return NotFound();

            return await CardDetails(cardId.Value);
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

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "-";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:N1} MB";
            return $"{bytes / 1024.0:N0} KB";
        }

        private static string FormatDateRange(DateTime? start, DateTime? end)
        {
            var th = CultureInfo.GetCultureInfo("th-TH");
            var startText = start?.ToString("dd MMM yyyy", th) ?? "-";
            var endText = end?.ToString("dd MMM yyyy", th) ?? "-";
            return $"{startText} - {endText}";
        }

        private static string FormatDate(DateTime? value)
        {
            var th = CultureInfo.GetCultureInfo("th-TH");
            return value?.ToString("dd MMM yyyy", th) ?? "-";
        }

        private async Task<List<RequirementLabelPayload>> LoadCardLabelPayloadAsync(int cardId)
        {
            return await _context.RequirementCardLabels
                .AsNoTracking()
                .Where(x => x.CardId == cardId && x.Label != null && x.Label.IsActive)
                .OrderBy(x => x.Label!.SortOrder)
                .ThenBy(x => x.Label!.LabelName)
                .Select(x => new RequirementLabelPayload
                {
                    LabelId = x.LabelId,
                    LabelName = x.Label!.LabelName,
                    ColorHex = x.Label!.ColorHex
                })
                .ToListAsync();
        }

        private async Task ReplaceCardLabelsAsync(int cardId, List<int>? labelIds)
        {
            var existingLabels = await _context.RequirementCardLabels
                .Where(x => x.CardId == cardId)
                .ToListAsync();

            if (existingLabels.Count > 0)
            {
                _context.RequirementCardLabels.RemoveRange(existingLabels);
            }

            var cleanIds = (labelIds ?? new List<int>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (cleanIds.Count == 0) return;

            var validIds = await _context.RequirementBoardLabels
                .Where(x => cleanIds.Contains(x.LabelId) && x.IsActive)
                .Select(x => x.LabelId)
                .ToListAsync();

            foreach (var labelId in validIds)
            {
                _context.RequirementCardLabels.Add(new RequirementCardLabel
                {
                    CardId = cardId,
                    LabelId = labelId,
                    CreatedAt = DateTime.Now
                });
            }
        }

        private async Task ReplacePhaseItemsAsync(int cardId, List<RequirementCardPhaseItemInput>? inputs)
        {
            var existingItems = await _context.RequirementCardPhaseItems
                .Where(x => x.CardId == cardId)
                .ToListAsync();

            if (existingItems.Count > 0)
            {
                _context.RequirementCardPhaseItems.RemoveRange(existingItems);
            }

            if (inputs == null || inputs.Count == 0) return;

            var now = DateTime.Now;
            var userId = CurrentUserId();
            var empId = CurrentEmpId();
            var sort = 1;

            foreach (var input in inputs)
            {
                var phaseName = (input.PhaseName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(phaseName)) continue;

                _context.RequirementCardPhaseItems.Add(new RequirementCardPhaseItem
                {
                    CardId = cardId,
                    PhaseName = phaseName,
                    PhaseType = NormalizePhaseType(input.PhaseType),
                    PhaseOrder = Math.Max(1, input.PhaseOrder),
                    PeriodOrder = Math.Max(1, input.PeriodOrder),
                    PhaseSort = sort++,
                    PhaseStatus = NormalizePhaseStatus(input.PhaseStatus),
                    PlanStart = ParseBoardDate(input.PlanStart),
                    PlanEnd = ParseBoardDate(input.PlanEnd),
                    PeriodEndDate = ParseBoardDate(input.PeriodEndDate),
                    CreatedByUserId = userId,
                    CreatedByEmpId = empId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        private static string NormalizePhaseType(string? value)
        {
            var text = (value ?? "").Trim().ToUpperInvariant();
            return text == "SUPPORT" ? "SUPPORT" : "MAIN";
        }

        private static string NormalizePhaseStatus(string? value)
        {
            var text = (value ?? "").Trim();
            return text switch
            {
                "กำลังดำเนินการ" => "กำลังดำเนินการ",
                "ส่งงวดงานแล้ว" => "ส่งงวดงานแล้ว",
                _ => "วางแผน"
            };
        }

        private static string NormalizeLabelColor(string? value)
        {
            var color = (value ?? "").Trim();
            if (!color.StartsWith("#", StringComparison.Ordinal))
            {
                color = "#" + color;
            }

            if (color.Length != 7) return "#22c7b8";

            for (var i = 1; i < color.Length; i++)
            {
                if (!Uri.IsHexDigit(color[i])) return "#22c7b8";
            }

            return color.ToLowerInvariant();
        }

        private static DateTime? ParseBoardDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            value = value.Trim();
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
            {
                return isoDate;
            }

            var parts = value.Split('/');
            if (parts.Length != 3) return null;

            if (!int.TryParse(parts[0], out var day) ||
                !int.TryParse(parts[1], out var month) ||
                !int.TryParse(parts[2], out var year))
            {
                return null;
            }

            if (year > 2400) year -= 543;

            try
            {
                return new DateTime(year, month, day);
            }
            catch
            {
                return null;
            }
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(
                Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsImageContentType(string? contentType)
        {
            return (contentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase);
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

            var userId = CurrentUserId();
            if (!userId.HasValue) return null;

            var fallbackEmpId = _context.LoginUsers
                .AsNoTracking()
                .Where(x => x.UserId == userId.Value)
                .Select(x => x.EmpId)
                .FirstOrDefault();

            if (fallbackEmpId.HasValue)
            {
                HttpContext.Session.SetInt32("EmpId", fallbackEmpId.Value);
            }

            return fallbackEmpId;
        }

        private sealed class RequirementLabelPayload
        {
            public int LabelId { get; set; }
            public string LabelName { get; set; } = "";
            public string ColorHex { get; set; } = "";
        }
    }
}
