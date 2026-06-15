using System.Text.Json;
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

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var signature = Request.Headers["X-Line-Signature"].ToString();

            if (!_lineMessaging.IsWebhookSignatureValid(body, signature))
            {
                _logger.LogWarning("Invalid LINE webhook signature");
                return Unauthorized();
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                return Ok();

            foreach (var lineEvent in events.EnumerateArray())
            {
                await HandleEventAsync(lineEvent, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return Ok();
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

            recipient.UserId = user.UserId;
            recipient.EmpId = user.EmpId;
            recipient.IsActive = true;
            recipient.UpdatedAt = now;

            if (!string.IsNullOrWhiteSpace(replyToken))
            {
                await _lineMessaging.ReplyTextAsync(
                    replyToken,
                    $"ผูก LINE กับบัญชี {user.Username} เรียบร้อยแล้ว",
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
    }
}
