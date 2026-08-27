using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using ProjectTracking.Data;
using ProjectTracking.Services;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    [RequireMenu("RequirementBoard.Index")]
    public class RequirementBoardController : BaseController
    {
        private const long MaxUploadSize = 209715200;
        private static readonly string[] BoardGradientCovers =
        {
            "g:ocean", "g:sky", "g:navy", "g:berry", "g:sunset", "g:forest",
            "g:violet", "g:flame", "g:lagoon", "g:steel", "g:midnight", "g:gold"
        };

        private static readonly string[] LegacyBoardCoverColors =
        {
            "#14b8a6", "#2563eb", "#0f4aa5", "#db2777", "#f59e0b", "#16a34a",
            "#7c3aed", "#ef4444", "#0891b2", "#64748b", "#111827", "#f97316", "#22c7b8"
        };

        private static readonly string[] BoardCoverImages =
        {
            "/images/boards/blue-city.svg",
            "/images/boards/night-mountain.svg",
            "/images/boards/orange-skyline.svg",
            "/images/boards/focus-lights.svg",
            "/images/boards/violet-tower.svg",
            "/images/boards/winter-field.svg",
            "/images/boards/aurora.svg",
            "/images/boards/coral-waves.svg",
            "/images/boards/green-field.svg",
            "/images/boards/blueprint.svg",
            "/images/boards/rose-clouds.svg",
            "/images/boards/midnight-grid.svg"
        };

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public RequirementBoardController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? cardId = null)
        {
            if (cardId.HasValue)
            {
                var targetBoardId = await _context.RequirementCards
                    .AsNoTracking()
                    .Where(x => x.CardId == cardId.Value && !x.IsArchived)
                    .Select(x => (int?)x.Column!.BoardId)
                    .FirstOrDefaultAsync();

                if (targetBoardId.HasValue)
                    return RedirectToAction(nameof(Board), new { id = targetBoardId.Value, cardId = cardId.Value });
            }

            await EnsureDefaultBoardShellAsync();

            var groups = await _context.RequirementBoardGroups
                .AsNoTracking()
                .AsSplitQuery()
                .Where(x => x.IsActive)
                .Include(x => x.Boards.Where(b => b.IsActive))
                    .ThenInclude(x => x.Columns)
                        .ThenInclude(x => x.Cards.Where(c => !c.IsArchived))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.GroupName)
                .ToListAsync();

            foreach (var group in groups)
            {
                group.Boards = group.Boards
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.BoardName)
                    .ToList();
            }

            var model = new RequirementBoardHomeViewModel
            {
                Groups = groups,
                OnlineUsers = await LoadOnlineUsersAsync(),
                TotalBoards = groups.Sum(x => x.Boards.Count),
                TotalCards = groups
                    .SelectMany(x => x.Boards)
                    .SelectMany(x => x.Columns)
                    .Sum(x => x.Cards.Count(c => !c.IsArchived))
            };
            ApplyPermissionFlags(model);

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Board(int id, int? cardId = null)
        {
            var board = await _context.RequirementBoards
                .AsNoTracking()
                .Include(x => x.Group)
                .FirstOrDefaultAsync(x => x.BoardId == id && x.IsActive && x.Group != null && x.Group.IsActive);

            if (board == null)
            {
                TempData["Error"] = "ไม่พบกระดานที่ต้องการ";
                return RedirectToAction(nameof(Index));
            }

            await EnsureDefaultColumnsAsync(board.BoardId);

            var columns = await _context.RequirementBoardColumns
                .AsNoTracking()
                .Where(x => x.BoardId == board.BoardId && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.ColumnId)
                .ToListAsync();

            var columnIds = columns.Select(x => x.ColumnId).ToList();
            var cards = await _context.RequirementCards
                .AsNoTracking()
                .AsSplitQuery()
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

            var labels = await _context.RequirementBoardLabels
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.LabelName)
                .ToListAsync();

            var model = new RequirementBoardViewModel
            {
                CurrentBoard = board,
                Columns = columns,
                OnlineUsers = await LoadOnlineUsersAsync(),
                Labels = labels,
                TotalCards = cards.Count,
                TotalAttachments = cards.Sum(x => x.Attachments.Count)
            };
            ApplyPermissionFlags(model);

            ViewBag.OpenCardId = cardId;
            var phaseStatusDefinitions = await _context.ProjectPhaseStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.StatusId)
                .ToListAsync();
            ViewBag.DefaultProjectPhaseStatus = phaseStatusDefinitions
                .FirstOrDefault(x => string.Equals(x.StatusCode, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))?.StatusDesc
                ?? phaseStatusDefinitions.FirstOrDefault()?.StatusDesc
                ?? string.Empty;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Create")]
        public async Task<IActionResult> CreateGroup(string groupName)
        {
            groupName = (groupName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                TempData["Error"] = "กรุณากรอกชื่อกลุ่ม";
                return RedirectToAction(nameof(Index));
            }

            var maxSort = await _context.RequirementBoardGroups
                .Select(x => (int?)x.SortOrder)
                .MaxAsync() ?? 0;

            _context.RequirementBoardGroups.Add(new RequirementBoardGroup
            {
                GroupName = groupName,
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
        [RequireMenu("RequirementBoard.Create")]
        public async Task<IActionResult> CreateBoard(int groupId, string boardName, string? coverChoice)
        {
            boardName = (boardName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(boardName))
            {
                TempData["Error"] = "กรุณากรอกชื่อกระดาน";
                return RedirectToAction(nameof(Index));
            }

            var groupExists = await _context.RequirementBoardGroups
                .AnyAsync(x => x.GroupId == groupId && x.IsActive);
            if (!groupExists)
            {
                TempData["Error"] = "ไม่พบกลุ่มสำหรับสร้างกระดาน";
                return RedirectToAction(nameof(Index));
            }

            var maxSort = await _context.RequirementBoards
                .Where(x => x.GroupId == groupId)
                .Select(x => (int?)x.SortOrder)
                .MaxAsync() ?? 0;

            var board = new RequirementBoard
            {
                GroupId = groupId,
                BoardName = boardName,
                SortOrder = maxSort + 1,
                IsActive = true,
                CreatedByUserId = CurrentUserId(),
                CreatedByEmpId = CurrentEmpId(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            ApplyBoardCover(board, coverChoice, useDefaultWhenMissing: true);

            _context.RequirementBoards.Add(board);
            await _context.SaveChangesAsync();
            await EnsureDefaultColumnsAsync(board.BoardId);

            return RedirectToAction(nameof(Board), new { id = board.BoardId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Edit")]
        public async Task<IActionResult> RenameGroup(int groupId, string groupName)
        {
            groupName = (groupName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                TempData["Error"] = "กรุณากรอกชื่อ group";
                return RedirectToAction(nameof(Index));
            }

            var group = await _context.RequirementBoardGroups
                .FirstOrDefaultAsync(x => x.GroupId == groupId && x.IsActive);
            if (group == null)
            {
                TempData["Error"] = "ไม่พบ group ที่ต้องการแก้ไข";
                return RedirectToAction(nameof(Index));
            }

            group.GroupName = groupName;
            group.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), null, null, $"group-{group.GroupId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Edit")]
        public async Task<IActionResult> RenameBoard(int boardId, string boardName, string? coverChoice)
        {
            boardName = (boardName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(boardName))
            {
                TempData["Error"] = "กรุณากรอกชื่อ board";
                return RedirectToAction(nameof(Index));
            }

            var board = await _context.RequirementBoards
                .FirstOrDefaultAsync(x => x.BoardId == boardId && x.IsActive);
            if (board == null)
            {
                TempData["Error"] = "ไม่พบ board ที่ต้องการแก้ไข";
                return RedirectToAction(nameof(Index));
            }

            board.BoardName = boardName;
            board.UpdatedAt = DateTime.Now;
            ApplyBoardCover(board, coverChoice, useDefaultWhenMissing: false);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), null, null, $"group-{board.GroupId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Delete")]
        public async Task<IActionResult> DeleteGroup(int groupId)
        {
            var group = await _context.RequirementBoardGroups
                .Include(x => x.Boards)
                .FirstOrDefaultAsync(x => x.GroupId == groupId && x.IsActive);
            if (group == null)
            {
                TempData["Error"] = "ไม่พบ group ที่ต้องการลบ";
                return RedirectToAction(nameof(Index));
            }

            var now = DateTime.Now;
            group.IsActive = false;
            group.UpdatedAt = now;

            foreach (var board in group.Boards)
            {
                board.IsActive = false;
                board.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Delete")]
        public async Task<IActionResult> DeleteBoard(int boardId)
        {
            var board = await _context.RequirementBoards
                .FirstOrDefaultAsync(x => x.BoardId == boardId && x.IsActive);
            if (board == null)
            {
                TempData["Error"] = "ไม่พบ board ที่ต้องการลบ";
                return RedirectToAction(nameof(Index));
            }

            board.IsActive = false;
            board.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), null, null, $"group-{board.GroupId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Create")]
        public async Task<IActionResult> CreateColumn(int boardId, string columnName)
        {
            columnName = (columnName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(columnName))
            {
                TempData["Error"] = "กรุณากรอกชื่อหัวข้อ";
                return boardId > 0
                    ? RedirectToAction(nameof(Board), new { id = boardId })
                    : RedirectToAction(nameof(Index));
            }

            if (!await _context.RequirementBoards.AnyAsync(x => x.BoardId == boardId && x.IsActive))
            {
                TempData["Error"] = "ไม่พบกระดานสำหรับเพิ่มหัวข้อ";
                return RedirectToAction(nameof(Index));
            }

            var maxSort = await _context.RequirementBoardColumns
                .Where(x => x.BoardId == boardId)
                .Select(x => (int?)x.SortOrder)
                .MaxAsync() ?? 0;

            _context.RequirementBoardColumns.Add(new RequirementBoardColumn
            {
                BoardId = boardId,
                ColumnName = columnName,
                SortOrder = maxSort + 1,
                IsActive = true,
                CreatedByUserId = CurrentUserId(),
                CreatedByEmpId = CurrentEmpId(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Board), new { id = boardId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Delete")]
        public async Task<IActionResult> DeleteColumn(int columnId)
        {
            var column = await _context.RequirementBoardColumns
                .Include(x => x.Board)
                .FirstOrDefaultAsync(x => x.ColumnId == columnId && x.IsActive && x.Board != null && x.Board.IsActive);

            if (column == null)
            {
                TempData["Error"] = "ไม่พบหัวข้อที่ต้องการลบ";
                return RedirectToAction(nameof(Index));
            }

            var activeCardCount = await _context.RequirementCards
                .CountAsync(x => x.ColumnId == column.ColumnId && !x.IsArchived);

            if (activeCardCount > 0)
            {
                TempData["Error"] = "หัวข้อนี้ยังมีการ์ดอยู่ กรุณาย้ายหรือลบการ์ดออกก่อน";
                return RedirectToAction(nameof(Board), new { id = column.BoardId });
            }

            var activeColumnCount = await _context.RequirementBoardColumns
                .CountAsync(x => x.BoardId == column.BoardId && x.IsActive);

            if (activeColumnCount <= 1)
            {
                TempData["Error"] = "ต้องมีหัวข้ออย่างน้อย 1 หัวข้อใน board";
                return RedirectToAction(nameof(Board), new { id = column.BoardId });
            }

            column.IsActive = false;
            column.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Board), new { id = column.BoardId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Create")]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadSize)]
        [RequestSizeLimit(MaxUploadSize)]
        public async Task<IActionResult> CreateCard(
            int columnId,
            string title,
            string? detail,
            IFormFile? coverImage,
            List<IFormFile>? files,
            List<int>? labelIds,
            List<RequirementCardPhaseItemInput>? phaseItems)
        {
            title = (title ?? "").Trim();
            var column = await _context.RequirementBoardColumns
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ColumnId == columnId && x.IsActive && x.Board != null && x.Board.IsActive);

            if (columnId <= 0 || column == null)
            {
                TempData["Error"] = "ไม่พบหัวข้อสำหรับเพิ่มการ์ด";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "กรุณากรอกหัวข้อการ์ด";
                return RedirectToAction(nameof(Board), new { id = column.BoardId });
            }

            if (!IsValidCoverImage(coverImage))
            {
                TempData["Error"] = "รูปพื้นหลังต้องเป็นไฟล์รูปภาพเท่านั้น";
                return RedirectToAction(nameof(Board), new { id = column.BoardId });
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
            await ReplaceCardLabelsAsync(card.CardId, labelIds);
            await ReplacePhaseItemsAsync(card.CardId, phaseItems);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Board), new { id = column.BoardId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Edit")]
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
            var card = await _context.RequirementCards
                .Include(x => x.Column)
                .FirstOrDefaultAsync(x => x.CardId == cardId && !x.IsArchived);
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
                return RedirectToAction(nameof(Board), new { id = card.Column?.BoardId });
            }

            card.Title = title;
            card.Detail = detail;
            card.UpdatedAt = DateTime.Now;

            if (!IsValidCoverImage(coverImage))
            {
                if (isAjax) return BadRequest(new { success = false, message = "รูปพื้นหลังต้องเป็นไฟล์รูปภาพเท่านั้น" });
                TempData["Error"] = "รูปพื้นหลังต้องเป็นไฟล์รูปภาพเท่านั้น";
                return RedirectToAction(nameof(Board), new { id = card.Column?.BoardId });
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
                var attachments = await LoadCardAttachmentPayloadAsync(card.CardId);

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
                    attachmentCount = attachments.Count,
                    attachments,
                    labels = await LoadCardLabelPayloadAsync(card.CardId)
                });
            }

            return RedirectToAction(nameof(Board), new { id = card.Column?.BoardId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Delete")]
        public async Task<IActionResult> ArchiveCard(int cardId)
        {
            var card = await _context.RequirementCards
                .Include(x => x.Column)
                .FirstOrDefaultAsync(x => x.CardId == cardId);
            if (card == null) return NotFound();

            card.IsArchived = true;
            card.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Board), new { id = card.Column?.BoardId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Delete")]
        public async Task<IActionResult> DeleteAttachment(int attachmentId)
        {
            var attachment = await _context.RequirementCardAttachments
                .Include(x => x.Card)
                    .ThenInclude(x => x!.Column)
                .FirstOrDefaultAsync(x => x.AttachmentId == attachmentId);
            if (attachment == null) return NotFound();

            DeletePhysicalFile(attachment.FilePath);
            _context.RequirementCardAttachments.Remove(attachment);

            var card = await _context.RequirementCards.FirstOrDefaultAsync(x => x.CardId == attachment.CardId);
            if (card != null) card.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Board), new { id = attachment.Card?.Column?.BoardId });
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
                .AsSplitQuery()
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

            var attachments = await LoadCardAttachmentPayloadAsync(card.CardId);

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
        [RequireMenu("RequirementBoard.Create")]
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
        [RequireMenu("RequirementBoard.Edit")]
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
        [RequireMenu("RequirementBoard.Edit")]
        public async Task<IActionResult> MoveCard([FromBody] MoveRequirementCardRequest request)
        {
            if (request == null || request.CardId <= 0 || request.ColumnId <= 0)
                return BadRequest(new { ok = false, message = "ข้อมูลไม่ครบ" });

            var targetColumn = await _context.RequirementBoardColumns
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ColumnId == request.ColumnId && x.IsActive);
            if (targetColumn == null)
                return BadRequest(new { ok = false, message = "ไม่พบหัวข้อปลายทาง" });

            var card = await _context.RequirementCards
                .Include(x => x.Column)
                .FirstOrDefaultAsync(x => x.CardId == request.CardId && !x.IsArchived);
            if (card == null) return NotFound(new { ok = false, message = "ไม่พบการ์ด" });
            if (card.Column != null && card.Column.BoardId != targetColumn.BoardId)
                return BadRequest(new { ok = false, message = "ไม่สามารถย้ายการ์ดข้ามกระดานได้" });

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("RequirementBoard.Edit")]
        public async Task<IActionResult> ReorderColumns([FromBody] ReorderRequirementColumnsRequest request)
        {
            var orderedIds = request?.OrderedColumnIds?
                .Where(x => x > 0)
                .Distinct()
                .ToList() ?? new List<int>();

            if (orderedIds.Count == 0)
                return BadRequest(new { ok = false, message = "ข้อมูลไม่ครบ" });

            var columns = await _context.RequirementBoardColumns
                .Where(x => orderedIds.Contains(x.ColumnId) && x.IsActive)
                .ToListAsync();

            if (columns.Count != orderedIds.Count)
                return BadRequest(new { ok = false, message = "พบหัวข้อที่ไม่ถูกต้อง" });

            if (columns.Select(x => x.BoardId).Distinct().Count() != 1)
                return BadRequest(new { ok = false, message = "ไม่สามารถสลับหัวข้อข้ามกระดานได้" });

            var now = DateTime.Now;
            for (var i = 0; i < orderedIds.Count; i++)
            {
                var column = columns.FirstOrDefault(x => x.ColumnId == orderedIds[i]);
                if (column == null) continue;

                column.SortOrder = i + 1;
                column.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        private async Task<RequirementBoard> EnsureDefaultBoardShellAsync()
        {
            var group = await _context.RequirementBoardGroups
                .FirstOrDefaultAsync(x => x.IsActive);

            var now = DateTime.Now;
            var userId = CurrentUserId();
            var empId = CurrentEmpId();

            if (group == null)
            {
                group = new RequirementBoardGroup
                {
                    GroupName = "Project Boards",
                    SortOrder = 1,
                    IsActive = true,
                    CreatedByUserId = userId,
                    CreatedByEmpId = empId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.RequirementBoardGroups.Add(group);
                await _context.SaveChangesAsync();
            }

            var board = await _context.RequirementBoards
                .FirstOrDefaultAsync(x => x.GroupId == group.GroupId && x.IsActive);

            if (board == null)
            {
                board = new RequirementBoard
                {
                    GroupId = group.GroupId,
                    BoardName = "Default Project Board",
                    CoverColor = BoardGradientCovers[0],
                    SortOrder = 1,
                    IsActive = true,
                    CreatedByUserId = userId,
                    CreatedByEmpId = empId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.RequirementBoards.Add(board);
                await _context.SaveChangesAsync();
            }

            await EnsureDefaultColumnsAsync(board.BoardId);
            return board;
        }

        private async Task EnsureDefaultColumnsAsync(int boardId)
        {
            if (await _context.RequirementBoardColumns.AnyAsync(x => x.BoardId == boardId && x.IsActive)) return;

            var now = DateTime.Now;
            var userId = CurrentUserId();
            var empId = CurrentEmpId();

            _context.RequirementBoardColumns.AddRange(
                new RequirementBoardColumn
                {
                    BoardId = boardId,
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
                    BoardId = boardId,
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

        private async Task<List<RequirementBoardOnlineUserViewModel>> LoadOnlineUsersAsync()
        {
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

            return onlineRows
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
        }

        private void ApplyPermissionFlags(RequirementBoardHomeViewModel model)
        {
            model.CanCreate = CanMenu("RequirementBoard.Create");
            model.CanEdit = CanMenu("RequirementBoard.Edit");
            model.CanDelete = CanMenu("RequirementBoard.Delete");
        }

        private void ApplyPermissionFlags(RequirementBoardViewModel model)
        {
            model.CanCreate = CanMenu("RequirementBoard.Create");
            model.CanEdit = CanMenu("RequirementBoard.Edit");
            model.CanDelete = CanMenu("RequirementBoard.Delete");
        }

        private bool CanMenu(string key)
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)) return true;

            var menus = HttpContext.Session.GetString("Menus") ?? "";
            return menus
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Contains(key, StringComparer.OrdinalIgnoreCase);
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

        private async Task<List<RequirementAttachmentPayload>> LoadCardAttachmentPayloadAsync(int cardId)
        {
            var files = await _context.RequirementCardAttachments
                .AsNoTracking()
                .Where(x => x.CardId == cardId)
                .OrderByDescending(x => x.UploadedAt)
                .Select(x => new
                {
                    x.AttachmentId,
                    x.FileName,
                    x.FileSize,
                    x.UploadedAt,
                    x.ContentType,
                    x.FilePath
                })
                .ToListAsync();

            return files
                .Select(file => new RequirementAttachmentPayload
                {
                    AttachmentId = file.AttachmentId,
                    FileName = file.FileName,
                    FileSize = FormatFileSize(file.FileSize),
                    UploadedAt = file.UploadedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                    IsImage = IsImageContentType(file.ContentType),
                    FilePath = file.FilePath,
                    PreviewUrl = Url.Action(nameof(PreviewAttachment), new { id = file.AttachmentId }) ?? "",
                    DownloadUrl = Url.Action(nameof(DownloadAttachment), new { id = file.AttachmentId }) ?? ""
                })
                .ToList();
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
            var statusDefinitions = await _context.ProjectPhaseStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.StatusId)
                .ToListAsync();
            var defaultStatus = statusDefinitions
                .FirstOrDefault(x => string.Equals(x.StatusCode, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
                ?? statusDefinitions.FirstOrDefault();

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
                    PhaseStatus = ResolvePhaseStatusDescription(statusDefinitions, input.PhaseStatus, defaultStatus),
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

        private static string ResolvePhaseStatusDescription(
            IReadOnlyCollection<ProjectPhaseStatusDefinition> definitions,
            string? value,
            ProjectPhaseStatusDefinition? fallback)
        {
            var text = (value ?? "").Trim();
            var definition = definitions.FirstOrDefault(x =>
                string.Equals(x.StatusCode, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.StatusDesc, text, StringComparison.OrdinalIgnoreCase));
            return definition?.StatusDesc ?? fallback?.StatusDesc ?? text;
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

        private static void ApplyBoardCover(RequirementBoard board, string? coverChoice, bool useDefaultWhenMissing)
        {
            var choice = (coverChoice ?? "").Trim();

            if (choice.StartsWith("image:", StringComparison.OrdinalIgnoreCase))
            {
                var imagePath = choice["image:".Length..].Trim();
                if (IsAllowedBoardCoverImage(imagePath))
                {
                    board.CoverImagePath = imagePath;
                    if (string.IsNullOrWhiteSpace(board.CoverColor))
                        board.CoverColor = BoardGradientCovers[0];
                    return;
                }
            }

            if (choice.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var color = choice["color:".Length..].Trim().ToLowerInvariant();
                if (BoardGradientCovers.Contains(color, StringComparer.OrdinalIgnoreCase))
                {
                    board.CoverColor = color;
                    board.CoverImagePath = null;
                    return;
                }

                var legacyColor = NormalizeLabelColor(color);
                if (LegacyBoardCoverColors.Contains(legacyColor, StringComparer.OrdinalIgnoreCase))
                {
                    board.CoverColor = legacyColor;
                    board.CoverImagePath = null;
                    return;
                }
            }

            if (useDefaultWhenMissing)
            {
                board.CoverColor = BoardGradientCovers[0];
                board.CoverImagePath = null;
            }
        }

        private static bool IsAllowedBoardCoverImage(string imagePath)
        {
            if (BoardCoverImages.Contains(imagePath, StringComparer.OrdinalIgnoreCase))
                return true;

            if (!Uri.TryCreate(imagePath, UriKind.Absolute, out var uri))
                return false;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(uri.Host, "source.unsplash.com", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Host, "images.unsplash.com", StringComparison.OrdinalIgnoreCase);
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
            => ProfileImagePathResolver.Normalize(profileImagePath);

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

        private sealed class RequirementAttachmentPayload
        {
            public int AttachmentId { get; set; }
            public string FileName { get; set; } = "";
            public string FileSize { get; set; } = "";
            public string UploadedAt { get; set; } = "";
            public bool IsImage { get; set; }
            public string FilePath { get; set; } = "";
            public string PreviewUrl { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
        }
    }
}
