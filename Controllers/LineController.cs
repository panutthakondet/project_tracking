using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    [ApiController]
    [Route("line")]
    public class LineController : ControllerBase
    {
        private static readonly ConcurrentQueue<LineDebugEntry> DebugEntries = new();
        private const int MaxDebugEntries = 50;

        private readonly AppDbContext _context;
        private readonly LineMessagingService _lineMessaging;
        private readonly ILogger<LineController> _logger;

        public LineController(
            AppDbContext context,
            LineMessagingService lineMessaging,
            ILogger<LineController> logger)
        {
            _context = context;
            _lineMessaging = lineMessaging;
            _logger = logger;
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                ok = true,
                webhook = "/line/webhook",
                hasChannelSecret = _lineMessaging.HasChannelSecret,
                hasChannelAccessToken = _lineMessaging.IsConfigured,
                hasAppBaseUrl = _lineMessaging.HasAppBaseUrl
            });
        }

        [HttpGet("debug")]
        public async Task<IActionResult> Debug(CancellationToken cancellationToken)
        {
            var recentRecipients = await _context.LineRecipients
                .AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .Take(10)
                .Select(x => new
                {
                    x.LineRecipientId,
                    x.UserId,
                    x.EmpId,
                    x.RecipientType,
                    HasLineUserId = !string.IsNullOrWhiteSpace(x.LineUserId),
                    x.IsActive,
                    x.LastWebhookAt,
                    x.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                ok = true,
                now = DateTime.Now,
                hasChannelSecret = _lineMessaging.HasChannelSecret,
                hasChannelAccessToken = _lineMessaging.IsConfigured,
                recentWebhookEvents = DebugEntries.Reverse().Take(MaxDebugEntries).ToList(),
                recentRecipients
            });
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var signature = Request.Headers["X-Line-Signature"].ToString();

            if (!_lineMessaging.IsWebhookSignatureValid(body, signature))
            {
                AddDebug("invalid_signature", bodyLength: body.Length, signaturePresent: !string.IsNullOrWhiteSpace(signature));
                _logger.LogWarning("Invalid LINE webhook signature");
                return Unauthorized();
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                {
                    AddDebug("no_events", bodyLength: body.Length, signaturePresent: true);
                    return Ok();
                }

                AddDebug("received", bodyLength: body.Length, signaturePresent: true, eventCount: events.GetArrayLength());

                foreach (var lineEvent in events.EnumerateArray())
                {
                    await HandleEventAsync(lineEvent, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                AddDebug("saved", bodyLength: body.Length, signaturePresent: true, eventCount: events.GetArrayLength());
                return Ok();
            }
            catch (Exception ex)
            {
                AddDebug("error", bodyLength: body.Length, signaturePresent: true, error: ex.Message);
                _logger.LogError(ex, "LINE webhook handling failed");
                return Ok();
            }
        }

        private async Task HandleEventAsync(JsonElement lineEvent, CancellationToken cancellationToken)
        {
            var eventType = ReadString(lineEvent, "type");
            var replyToken = ReadString(lineEvent, "replyToken");

            if (!lineEvent.TryGetProperty("source", out var source))
                return;

            var sourceType = ReadString(source, "type");
            var lineUserId = ReadString(source, "userId");
            var lineGroupId = ReadString(source, "groupId");
            var now = DateTime.Now;

            AddDebug(
                "event",
                eventType: eventType,
                sourceType: sourceType,
                lineUserId: MaskLineId(lineUserId),
                groupId: MaskLineId(lineGroupId));

            if (sourceType == "group" && !string.IsNullOrWhiteSpace(lineGroupId))
            {
                UpsertGroup(lineGroupId, now);
                if (eventType == "join" && !string.IsNullOrWhiteSpace(replyToken))
                {
                    await _lineMessaging.ReplyTextAsync(
                        replyToken,
                        "เชื่อมต่อกลุ่มกับ ProjectTrackingAlert แล้ว",
                        cancellationToken);
                }
                return;
            }

            if (sourceType != "user" || string.IsNullOrWhiteSpace(lineUserId))
                return;

            var recipient = UpsertUser(lineUserId, now, eventType == "follow");

            if (eventType == "follow" && !string.IsNullOrWhiteSpace(replyToken))
            {
                await _lineMessaging.ReplyTextAsync(
                    replyToken,
                    "เพิ่มเพื่อนเรียบร้อยครับ ส่งข้อความว่า LINK username เพื่อผูก LINE กับบัญชี Project Tracking",
                    cancellationToken);
                return;
            }

            if (eventType != "message" || !TryReadTextMessage(lineEvent, out var text))
                return;

            var username = ParseLinkUsername(text);
            AddDebug("message", eventType: eventType, sourceType: sourceType, lineUserId: MaskLineId(lineUserId), text: MaskMessage(text), username: username);
            if (string.IsNullOrWhiteSpace(username))
            {
                if (!string.IsNullOrWhiteSpace(replyToken))
                {
                    await _lineMessaging.ReplyTextAsync(
                        replyToken,
                        "หากต้องการรับแจ้งเตือน ส่งข้อความว่า LINK username เช่น LINK somchai",
                        cancellationToken);
                }
                return;
            }

            var user = await _context.LoginUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.Status == "ACTIVE", cancellationToken);

            if (user == null)
            {
                if (!string.IsNullOrWhiteSpace(replyToken))
                {
                    await _lineMessaging.ReplyTextAsync(
                        replyToken,
                        $"ไม่พบ username: {username}",
                        cancellationToken);
                }
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
            AddDebug("linked", eventType: eventType, sourceType: sourceType, lineUserId: MaskLineId(lineUserId), username: user.Username, empId: empId);

            if (!string.IsNullOrWhiteSpace(replyToken))
            {
                var empText = empId.HasValue ? $" (EmpId: {empId.Value})" : "";
                await _lineMessaging.ReplyTextAsync(
                    replyToken,
                    $"ผูก LINE กับบัญชี {user.Username}{empText} เรียบร้อยแล้ว",
                    cancellationToken);
            }
        }

        private LineRecipient UpsertUser(string lineUserId, DateTime now, bool followed)
        {
            var recipient = _context.LineRecipients
                .FirstOrDefault(x => x.LineUserId == lineUserId && x.RecipientType == "USER");

            if (recipient != null)
            {
                recipient.IsActive = true;
                recipient.LastWebhookAt = now;
                recipient.UpdatedAt = now;
                if (followed)
                    recipient.LastFollowedAt = now;
                return recipient;
            }

            recipient = new LineRecipient
            {
                RecipientType = "USER",
                LineUserId = lineUserId,
                IsActive = true,
                LastFollowedAt = followed ? now : null,
                LastWebhookAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.LineRecipients.Add(recipient);
            return recipient;
        }

        private void UpsertGroup(string lineGroupId, DateTime now)
        {
            var recipient = _context.LineRecipients
                .FirstOrDefault(x => x.LineGroupId == lineGroupId && x.RecipientType == "GROUP");

            if (recipient != null)
            {
                recipient.IsActive = true;
                recipient.LastWebhookAt = now;
                recipient.UpdatedAt = now;
                return;
            }

            _context.LineRecipients.Add(new LineRecipient
            {
                RecipientType = "GROUP",
                LineGroupId = lineGroupId,
                IsActive = true,
                LastWebhookAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        private static bool TryReadTextMessage(JsonElement lineEvent, out string text)
        {
            text = "";
            if (!lineEvent.TryGetProperty("message", out var message))
                return false;

            if (ReadString(message, "type") != "text")
                return false;

            text = ReadString(message, "text") ?? "";
            return !string.IsNullOrWhiteSpace(text);
        }

        private static string? ParseLinkUsername(string text)
        {
            var normalized = (text ?? "").Trim();
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

        private static void AddDebug(
            string stage,
            int? bodyLength = null,
            bool? signaturePresent = null,
            int? eventCount = null,
            string? eventType = null,
            string? sourceType = null,
            string? lineUserId = null,
            string? groupId = null,
            string? text = null,
            string? username = null,
            int? empId = null,
            string? error = null)
        {
            DebugEntries.Enqueue(new LineDebugEntry(
                DateTime.Now,
                stage,
                bodyLength,
                signaturePresent,
                eventCount,
                eventType,
                sourceType,
                lineUserId,
                groupId,
                text,
                username,
                empId,
                error));

            while (DebugEntries.Count > MaxDebugEntries && DebugEntries.TryDequeue(out _))
            {
            }
        }

        private static string? MaskLineId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Length <= 8
                ? "***"
                : $"{value[..4]}...{value[^4..]}";
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

        private sealed record LineDebugEntry(
            DateTime At,
            string Stage,
            int? BodyLength,
            bool? SignaturePresent,
            int? EventCount,
            string? EventType,
            string? SourceType,
            string? LineUserId,
            string? GroupId,
            string? Text,
            string? Username,
            int? EmpId,
            string? Error);
    }
}
