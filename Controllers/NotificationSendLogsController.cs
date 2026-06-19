using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;
using System.Net;
using System.Text.RegularExpressions;

namespace ProjectTracking.Controllers
{
    public class NotificationSendLogsController : BaseController
    {
        private const string DefaultProfileImagePath = "/images/Profile/profile.png";
        private readonly AppDbContext _context;

        public NotificationSendLogsController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("NotificationSendLogs.Index")]
        public async Task<IActionResult> Index(string? channel = null, int? empId = null)
        {
            var selectedChannel = NormalizeChannel(channel);
            var employees = await LoadUsersAsync();
            var employeeById = employees.ToDictionary(x => x.EmpId);

            var tabs = await BuildTabsAsync();
            var countRows = await _context.NotificationSendLogs
                .AsNoTracking()
                .Where(x => x.Channel == selectedChannel && x.RecipientEmpId.HasValue)
                .GroupBy(x => x.RecipientEmpId!.Value)
                .Select(x => new
                {
                    EmpId = x.Key,
                    Count = x.Count(),
                    LastSentAt = x.Max(row => row.SentAt)
                })
                .ToListAsync(HttpContext.RequestAborted);

            var countByEmpId = countRows.ToDictionary(x => x.EmpId, x => x);
            foreach (var user in employees)
            {
                if (countByEmpId.TryGetValue(user.EmpId, out var stat))
                {
                    user.Count = stat.Count;
                    user.LastSentAt = stat.LastSentAt;
                }
            }

            var logQuery = _context.NotificationSendLogs
                .AsNoTracking()
                .Where(x => x.Channel == selectedChannel);

            if (empId.HasValue && empId.Value > 0)
                logQuery = logQuery.Where(x => x.RecipientEmpId == empId.Value);

            var logs = await logQuery
                .OrderByDescending(x => x.SentAt)
                .ThenByDescending(x => x.LogId)
                .Take(300)
                .ToListAsync(HttpContext.RequestAborted);

            var model = new NotificationSendLogPageViewModel
            {
                SelectedChannel = selectedChannel,
                SelectedEmpId = empId,
                Tabs = tabs,
                Users = employees
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Name)
                    .ToList(),
                Logs = logs.Select(log => ToLogItem(log, employeeById)).ToList()
            };

            return View(model);
        }

        private async Task<List<NotificationSendLogTabViewModel>> BuildTabsAsync()
        {
            var counts = await _context.NotificationSendLogs
                .AsNoTracking()
                .GroupBy(x => x.Channel)
                .Select(x => new { Channel = x.Key, Count = x.Count() })
                .ToListAsync(HttpContext.RequestAborted);

            var countByChannel = counts.ToDictionary(x => x.Channel, x => x.Count, StringComparer.OrdinalIgnoreCase);
            return new List<NotificationSendLogTabViewModel>
            {
                new() { Channel = "EMAIL", Label = "Mail", Count = countByChannel.GetValueOrDefault("EMAIL") },
                new() { Channel = "LINE", Label = "LINE", Count = countByChannel.GetValueOrDefault("LINE") },
                new() { Channel = "TELEGRAM", Label = "Telegram", Count = countByChannel.GetValueOrDefault("TELEGRAM") }
            };
        }

        private async Task<List<NotificationSendLogUserViewModel>> LoadUsersAsync()
        {
            var employees = await _context.Employees
                .AsNoTracking()
                .Include(x => x.LoginUser)
                .Where(x => x.Status == "ACTIVE")
                .OrderBy(x => x.EmpName)
                .ToListAsync(HttpContext.RequestAborted);

            var loginRows = await _context.LoginUsers
                .AsNoTracking()
                .Where(x => x.EmpId.HasValue)
                .OrderBy(x => x.UserId)
                .ToListAsync(HttpContext.RequestAborted);
            var loginsByEmpId = loginRows
                .GroupBy(x => x.EmpId!.Value)
                .ToDictionary(x => x.Key, x => x.First());

            return employees.Select(employee =>
            {
                var login = employee.LoginUser
                    ?? (loginsByEmpId.TryGetValue(employee.EmpId, out var user) ? user : null);

                return new NotificationSendLogUserViewModel
                {
                    EmpId = employee.EmpId,
                    Name = employee.EmpName,
                    Username = string.IsNullOrWhiteSpace(login?.Username) ? "-" : login.Username,
                    Position = string.IsNullOrWhiteSpace(employee.Position) ? "-" : employee.Position!,
                    AvatarPath = ProfileImage(login?.ProfileImagePath)
                };
            }).ToList();
        }

        private static NotificationSendLogItemViewModel ToLogItem(
            NotificationSendLog log,
            IReadOnlyDictionary<int, NotificationSendLogUserViewModel> employeeById)
        {
            employeeById.TryGetValue(log.RecipientEmpId ?? 0, out var employee);
            return new NotificationSendLogItemViewModel
            {
                LogId = log.LogId,
                Channel = log.Channel,
                RecipientEmpId = log.RecipientEmpId,
                RecipientName = employee?.Name ?? "-",
                RecipientAddress = string.IsNullOrWhiteSpace(log.RecipientAddress) ? "-" : log.RecipientAddress!,
                AvatarPath = employee?.AvatarPath ?? DefaultProfileImagePath,
                Title = string.IsNullOrWhiteSpace(log.Title) ? "-" : log.Title,
                Message = ToPlainText(log.Message),
                TargetUrl = log.TargetUrl,
                SentAt = log.SentAt
            };
        }

        private static string NormalizeChannel(string? channel)
        {
            var normalized = (channel ?? "LINE").Trim().ToUpperInvariant();
            return normalized switch
            {
                "MAIL" or "EMAIL" => "EMAIL",
                "TELEGRAM" => "TELEGRAM",
                _ => "LINE"
            };
        }

        private static string ToPlainText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            var text = value.Trim();
            text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</\s*(p|div|li|tr|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*/?\s*(p|div|li|tr|td|th|h[1-6])\b[^>]*>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*a\b[^>]*>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</\s*a\s*>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", "", RegexOptions.IgnoreCase);
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"[ \t]{2,}", " ");
            text = Regex.Replace(text, @"[ \t]*\n[ \t]*", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            return string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
        }

        private static string ProfileImage(string? profileImagePath)
        {
            if (string.IsNullOrWhiteSpace(profileImagePath))
                return DefaultProfileImagePath;

            var path = profileImagePath.Trim();
            return path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";
        }
    }
}
