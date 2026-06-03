using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class MailboxController : BaseController
    {
        private readonly AppDbContext _context;

        public MailboxController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var userId = CurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Auth");

            var rows = await _context.MailboxRecipients
                .AsNoTracking()
                .Include(x => x.Message!)
                    .ThenInclude(x => x.SenderEmployee)
                .Include(x => x.Message!)
                    .ThenInclude(x => x.SenderUser)
                .Include(x => x.Message!)
                    .ThenInclude(x => x.Report)
                .Where(x => x.RecipientUserId == userId.Value && !x.IsDeleted)
                .OrderBy(x => x.IsRead)
                .ThenByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();

            var model = rows.Select(x => new MailboxIndexItemViewModel
            {
                MessageId = x.MessageId,
                Subject = x.Message?.Subject ?? "-",
                Body = x.Message?.Body,
                SenderName = x.Message?.SenderEmployee?.EmpName ?? x.Message?.SenderUser?.Username ?? "ระบบ",
                MessageType = x.Message?.MessageType ?? "GENERAL",
                IsRead = x.IsRead,
                CreatedAt = x.Message?.CreatedAt ?? x.CreatedAt,
                ReportId = x.Message?.ReportId,
                ReportStatus = x.Message?.Report?.Status
            }).ToList();

            return View(model);
        }

        public async Task<IActionResult> Sent()
        {
            var userId = CurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Auth");

            var messages = await _context.MailboxMessages
                .AsNoTracking()
                .Include(x => x.Report)
                .Include(x => x.Recipients)
                    .ThenInclude(x => x.RecipientEmployee)
                .Where(x => x.SenderUserId == userId.Value)
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();

            return View(messages);
        }

        public async Task<IActionResult> Details(int id)
        {
            var userId = CurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Auth");

            var message = await _context.MailboxMessages
                .Include(x => x.SenderEmployee)
                .Include(x => x.SenderUser)
                .Include(x => x.Recipients)
                    .ThenInclude(x => x.RecipientEmployee)
                .Include(x => x.Report)
                    .ThenInclude(x => x!.Attachments)
                .FirstOrDefaultAsync(x => x.MessageId == id);

            if (message == null) return NotFound();

            var isRecipient = message.Recipients.Any(x => x.RecipientUserId == userId.Value && !x.IsDeleted);
            var isSender = message.SenderUserId == userId.Value;
            if (!isRecipient && !isSender && !IsAdmin()) return Forbid();

            var recipientRow = await _context.MailboxRecipients
                .FirstOrDefaultAsync(x => x.MessageId == id && x.RecipientUserId == userId.Value && !x.IsDeleted);

            if (recipientRow != null && !recipientRow.IsRead)
            {
                recipientRow.IsRead = true;
                recipientRow.ReadAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            var model = new MailboxDetailsViewModel
            {
                Message = message,
                Report = message.Report,
                Attachments = message.Report?.Attachments.OrderByDescending(x => x.UploadedAt).ToList() ?? new List<WeeklyReportAttachment>(),
                Users = await LoadUserOptionsAsync(excludeUserId: userId.Value),
                CanForward = message.ReportId.HasValue
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = CurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Auth");

            var recipientRow = await _context.MailboxRecipients
                .FirstOrDefaultAsync(x => x.MessageId == id && x.RecipientUserId == userId.Value);

            if (recipientRow != null)
            {
                recipientRow.IsDeleted = true;
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<List<UserOptionViewModel>> LoadUserOptionsAsync(int? excludeUserId = null)
        {
            var users = await _context.LoginUsers
                .AsNoTracking()
                .Where(x => x.Status == "ACTIVE")
                .ToListAsync();

            var empIds = users.Where(x => x.EmpId.HasValue).Select(x => x.EmpId!.Value).ToHashSet();
            var userIds = users.Select(x => x.UserId).ToHashSet();

            var employees = await _context.Employees
                .AsNoTracking()
                .Where(x => empIds.Contains(x.EmpId) || (x.LoginUserId.HasValue && userIds.Contains(x.LoginUserId.Value)))
                .ToListAsync();

            return users
                .Where(x => !excludeUserId.HasValue || x.UserId != excludeUserId.Value)
                .Select(user =>
                {
                    var employee = employees.FirstOrDefault(e => user.EmpId.HasValue && e.EmpId == user.EmpId.Value)
                        ?? employees.FirstOrDefault(e => e.LoginUserId == user.UserId);
                    return new UserOptionViewModel
                    {
                        UserId = user.UserId,
                        EmpId = employee?.EmpId,
                        Username = user.Username,
                        DisplayName = employee?.EmpName ?? user.Username,
                        Position = employee?.Position
                    };
                })
                .OrderBy(x => GetPositionOrder(x.Position))
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

        private int? CurrentUserId()
            => HttpContext.Session.GetInt32("UserId");

        private bool IsAdmin()
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            return string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPositionOrder(string? position)
        {
            var value = (position ?? "").Trim().ToUpperInvariant();
            if (value.Contains("BUSINESS DEVELOPMENT")) return 10;
            if (value.Contains("PROJECT MANAGER")) return 20;
            if (value.Contains("BUSINESS ANALYST")) return 30;
            return 90;
        }
    }
}
