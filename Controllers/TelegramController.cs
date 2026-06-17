using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    [ApiController]
    [Route("telegram")]
    public class TelegramController : ControllerBase
    {
        private static readonly ConcurrentQueue<TelegramDebugEntry> DebugEntries = new();
        private const int MaxDebugEntries = 50;

        private readonly AppDbContext _context;
        private readonly TelegramMessagingService _telegramMessaging;
        private readonly ILogger<TelegramController> _logger;

        public TelegramController(
            AppDbContext context,
            TelegramMessagingService telegramMessaging,
            ILogger<TelegramController> logger)
        {
            _context = context;
            _telegramMessaging = telegramMessaging;
            _logger = logger;
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                ok = true,
                webhook = "/telegram/webhook",
                hasBotToken = _telegramMessaging.IsConfigured,
                hasWebhookSecretToken = _telegramMessaging.HasWebhookSecretToken,
                hasAppBaseUrl = _telegramMessaging.HasAppBaseUrl,
                botTokenFingerprint = _telegramMessaging.BotTokenFingerprint,
                webhookSecretTokenFingerprint = _telegramMessaging.WebhookSecretTokenFingerprint
            });
        }

        [HttpGet("debug")]
        public async Task<IActionResult> Debug(CancellationToken cancellationToken)
        {
            var recentRecipients = await _context.TelegramRecipients
                .AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .Take(10)
                .Select(x => new
                {
                    x.TelegramRecipientId,
                    x.UserId,
                    x.EmpId,
                    x.RecipientType,
                    HasTelegramChatId = !string.IsNullOrWhiteSpace(x.TelegramChatId),
                    x.IsActive,
                    x.LastWebhookAt,
                    x.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                ok = true,
                now = DateTime.Now,
                hasBotToken = _telegramMessaging.IsConfigured,
                hasWebhookSecretToken = _telegramMessaging.HasWebhookSecretToken,
                botTokenFingerprint = _telegramMessaging.BotTokenFingerprint,
                webhookSecretTokenFingerprint = _telegramMessaging.WebhookSecretTokenFingerprint,
                recentWebhookEvents = DebugEntries.Reverse().Take(MaxDebugEntries).ToList(),
                recentRecipients
            });
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
        {
            var secretHeader = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            if (!_telegramMessaging.IsWebhookSecretValid(secretHeader))
            {
                AddDebug("invalid_secret", secretPresent: !string.IsNullOrWhiteSpace(secretHeader));
                _logger.LogWarning("Invalid Telegram webhook secret token");
                return Unauthorized();
            }

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync(cancellationToken);
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (!TryGetMessage(root, out var message))
                {
                    AddDebug("no_message", bodyLength: body.Length, secretPresent: !string.IsNullOrWhiteSpace(secretHeader));
                    return Ok();
                }

                await HandleMessageAsync(message, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                AddDebug("saved", bodyLength: body.Length, secretPresent: !string.IsNullOrWhiteSpace(secretHeader));
                return Ok();
            }
            catch (Exception ex)
            {
                AddDebug("error", secretPresent: !string.IsNullOrWhiteSpace(secretHeader), error: ex.Message);
                _logger.LogError(ex, "Telegram webhook handling failed");
                return Ok();
            }
        }

        private async Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
        {
            if (!message.TryGetProperty("chat", out var chat))
                return;

            var chatId = ReadId(chat, "id");
            var chatType = ReadString(chat, "type") ?? "";
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            var now = DateTime.Now;
            var text = ReadString(message, "text") ?? "";

            AddDebug(
                "message",
                chatType: chatType,
                chatId: MaskId(chatId),
                text: MaskMessage(text));

            if (chatType is "group" or "supergroup")
            {
                UpsertGroup(chatId, ReadChatDisplayName(chat), now);
                return;
            }

            if (chatType != "private")
                return;

            var telegramUserId = message.TryGetProperty("from", out var from)
                ? ReadId(from, "id")
                : chatId;
            var displayName = message.TryGetProperty("from", out var fromForName)
                ? ReadUserDisplayName(fromForName)
                : ReadChatDisplayName(chat);
            var isStart = text.TrimStart().StartsWith("/start", StringComparison.OrdinalIgnoreCase);
            var recipient = UpsertUser(telegramUserId ?? chatId, chatId, displayName, now, isStart);

            var username = ParseLinkUsername(text);
            if (isStart && string.IsNullOrWhiteSpace(username))
            {
                await _telegramMessaging.SendTextToChatAsync(
                    chatId,
                    "เริ่มใช้งาน Telegram Notification แล้วครับ ส่งข้อความว่า LINK username เพื่อผูก Telegram กับบัญชี Project Tracking",
                    cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                await _telegramMessaging.SendTextToChatAsync(
                    chatId,
                    "หากต้องการรับแจ้งเตือน ส่งข้อความว่า LINK username เช่น LINK somchai",
                    cancellationToken);
                return;
            }

            var user = await _context.LoginUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.Status == "ACTIVE", cancellationToken);

            if (user == null)
            {
                await _telegramMessaging.SendTextToChatAsync(
                    chatId,
                    $"ไม่พบ username: {username}",
                    cancellationToken);
                return;
            }

            var empId = user.EmpId ?? await _context.Employees
                .AsNoTracking()
                .Where(x => x.LoginUserId == user.UserId)
                .Select(x => (int?)x.EmpId)
                .FirstOrDefaultAsync(cancellationToken);

            recipient.UserId = user.UserId;
            recipient.EmpId = empId;
            recipient.IsActive = true;
            recipient.UpdatedAt = now;
            AddDebug("linked", chatType: chatType, chatId: MaskId(chatId), username: user.Username, empId: empId);

            var empText = empId.HasValue ? $" (EmpId: {empId.Value})" : "";
            await _telegramMessaging.SendTextToChatAsync(
                chatId,
                $"ผูก Telegram กับบัญชี {user.Username}{empText} เรียบร้อยแล้ว",
                cancellationToken);
        }

        private TelegramRecipient UpsertUser(
            string telegramUserId,
            string chatId,
            string? displayName,
            DateTime now,
            bool started)
        {
            var recipient = _context.TelegramRecipients
                .FirstOrDefault(x => x.RecipientType == "USER"
                    && (x.TelegramUserId == telegramUserId || x.TelegramChatId == chatId));

            if (recipient != null)
            {
                recipient.TelegramUserId = telegramUserId;
                recipient.TelegramChatId = chatId;
                recipient.TelegramDisplayName = displayName;
                recipient.IsActive = true;
                recipient.LastWebhookAt = now;
                recipient.UpdatedAt = now;
                if (started)
                    recipient.LastStartedAt = now;
                return recipient;
            }

            recipient = new TelegramRecipient
            {
                RecipientType = "USER",
                TelegramUserId = telegramUserId,
                TelegramChatId = chatId,
                TelegramDisplayName = displayName,
                IsActive = true,
                LastStartedAt = started ? now : null,
                LastWebhookAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.TelegramRecipients.Add(recipient);
            return recipient;
        }

        private void UpsertGroup(string chatId, string? displayName, DateTime now)
        {
            var recipient = _context.TelegramRecipients
                .FirstOrDefault(x => x.TelegramChatId == chatId && x.RecipientType == "GROUP");

            if (recipient != null)
            {
                recipient.TelegramDisplayName = displayName;
                recipient.IsActive = true;
                recipient.LastWebhookAt = now;
                recipient.UpdatedAt = now;
                return;
            }

            _context.TelegramRecipients.Add(new TelegramRecipient
            {
                RecipientType = "GROUP",
                TelegramChatId = chatId,
                TelegramDisplayName = displayName,
                IsActive = true,
                LastWebhookAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        private static bool TryGetMessage(JsonElement root, out JsonElement message)
        {
            if (root.TryGetProperty("message", out message)
                || root.TryGetProperty("edited_message", out message)
                || root.TryGetProperty("channel_post", out message))
            {
                return true;
            }

            message = default;
            return false;
        }

        private static string? ParseLinkUsername(string text)
        {
            var normalized = (text ?? "").Trim();
            if (normalized.StartsWith("/start ", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[7..].Trim();

            if (normalized.StartsWith("LINK ", StringComparison.OrdinalIgnoreCase))
                return normalized[5..].Trim();

            if (normalized.StartsWith("ผูก ", StringComparison.OrdinalIgnoreCase))
                return normalized[4..].Trim();

            return null;
        }

        private static string? ReadString(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string? ReadId(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetInt64(out var number) ? number.ToString() : value.GetRawText(),
                JsonValueKind.String => value.GetString(),
                _ => null
            };
        }

        private static string ReadUserDisplayName(JsonElement user)
        {
            var nameParts = new[]
            {
                ReadString(user, "first_name"),
                ReadString(user, "last_name")
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToList();

            var displayName = string.Join(" ", nameParts);
            var username = ReadString(user, "username");
            if (!string.IsNullOrWhiteSpace(username))
                displayName = string.IsNullOrWhiteSpace(displayName) ? $"@{username}" : $"{displayName} (@{username})";

            return string.IsNullOrWhiteSpace(displayName) ? "Telegram user" : displayName;
        }

        private static string ReadChatDisplayName(JsonElement chat)
        {
            return ReadString(chat, "title")
                ?? ReadString(chat, "username")
                ?? string.Join(" ", new[] { ReadString(chat, "first_name"), ReadString(chat, "last_name") }.Where(x => !string.IsNullOrWhiteSpace(x)))
                ?? "Telegram chat";
        }

        private static void AddDebug(
            string stage,
            int? bodyLength = null,
            bool? secretPresent = null,
            string? chatType = null,
            string? chatId = null,
            string? text = null,
            string? username = null,
            int? empId = null,
            string? error = null)
        {
            DebugEntries.Enqueue(new TelegramDebugEntry(
                DateTime.Now,
                stage,
                bodyLength,
                secretPresent,
                chatType,
                chatId,
                text,
                username,
                empId,
                error));

            while (DebugEntries.Count > MaxDebugEntries && DebugEntries.TryDequeue(out _))
            {
            }
        }

        private static string? MaskId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Length <= 6
                ? "***"
                : $"{value[..3]}...{value[^3..]}";
        }

        private static string? MaskMessage(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            return trimmed.StartsWith("LINK ", StringComparison.OrdinalIgnoreCase)
                ? "LINK ***"
                : trimmed.Length <= 30 ? trimmed : $"{trimmed[..30]}...";
        }

        private sealed record TelegramDebugEntry(
            DateTime At,
            string Stage,
            int? BodyLength,
            bool? SecretPresent,
            string? ChatType,
            string? ChatId,
            string? Text,
            string? Username,
            int? EmpId,
            string? Error);
    }
}
