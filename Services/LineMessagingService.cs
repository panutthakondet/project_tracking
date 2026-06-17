using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectTracking.Data;
using ProjectTracking.Models;
using Microsoft.EntityFrameworkCore;

namespace ProjectTracking.Services
{
    public class LineMessagingService
    {
        private const string PushEndpoint = "https://api.line.me/v2/bot/message/push";
        private const string ReplyEndpoint = "https://api.line.me/v2/bot/message/reply";

        private readonly HttpClient _httpClient;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<LineMessagingService> _logger;
        private readonly string _channelAccessToken;
        private readonly string _channelSecret;
        private readonly string _appBaseUrl;

        public LineMessagingService(
            HttpClient httpClient,
            IDbContextFactory<AppDbContext> dbFactory,
            IConfiguration configuration,
            ILogger<LineMessagingService> logger)
        {
            _httpClient = httpClient;
            _dbFactory = dbFactory;
            _logger = logger;
            _channelAccessToken = Environment.GetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN")
                ?? configuration["LINE_CHANNEL_ACCESS_TOKEN"]
                ?? "";
            _channelSecret = Environment.GetEnvironmentVariable("LINE_CHANNEL_SECRET")
                ?? configuration["LINE_CHANNEL_SECRET"]
                ?? "";
            _appBaseUrl = (Environment.GetEnvironmentVariable("APP_BASE_URL")
                ?? configuration["APP_BASE_URL"]
                ?? "").TrimEnd('/');
        }

        public bool IsConfigured
            => !string.IsNullOrWhiteSpace(_channelAccessToken);

        public bool HasChannelSecret
            => !string.IsNullOrWhiteSpace(_channelSecret);

        public bool HasAppBaseUrl
            => !string.IsNullOrWhiteSpace(_appBaseUrl);

        public string ChannelSecretFingerprint
            => Fingerprint(_channelSecret);

        public string ChannelAccessTokenFingerprint
            => Fingerprint(_channelAccessToken);

        public bool IsWebhookSignatureValid(string body, string? signature)
        {
            if (string.IsNullOrWhiteSpace(_channelSecret) || string.IsNullOrWhiteSpace(signature))
            {
                _logger.LogWarning(
                    "LINE webhook signature validation skipped. HasSecret={HasSecret}, HasSignature={HasSignature}",
                    !string.IsNullOrWhiteSpace(_channelSecret),
                    !string.IsNullOrWhiteSpace(signature));
                return false;
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_channelSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            var expected = Convert.ToBase64String(hash);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature));
        }

        public async Task<int> SendNotificationToEmployeeAsync(
            int empId,
            string title,
            string? message,
            string? targetUrl,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return 0;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var lineUserIds = await db.LineRecipients
                .AsNoTracking()
                .Where(x => x.IsActive
                    && x.EmpId == empId
                    && x.RecipientType == "USER"
                    && x.LineUserId != null
                    && x.LineUserId != "")
                .Select(x => x.LineUserId!)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (lineUserIds.Count == 0)
                return 0;

            var absoluteUrl = ToAbsoluteUrl(targetUrl);
            if (!string.IsNullOrWhiteSpace(targetUrl) && string.IsNullOrWhiteSpace(absoluteUrl))
            {
                _logger.LogWarning(
                    "LINE notification target URL could not be converted to absolute URL. TargetUrl={TargetUrl}, HasAppBaseUrl={HasAppBaseUrl}",
                    targetUrl,
                    HasAppBaseUrl);
            }

            var text = BuildNotificationText(title, message, absoluteUrl);
            var flexMessage = BuildNotificationFlexMessage(title, message, absoluteUrl);
            foreach (var lineUserId in lineUserIds)
            {
                try
                {
                    await PushMessageAsync(lineUserId, flexMessage, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LINE Flex notification failed. Falling back to text for EmpId={EmpId}", empId);
                    await PushTextAsync(lineUserId, text, cancellationToken);
                }
            }

            return lineUserIds.Count;
        }

        public async Task PushTextAsync(string to, string text, CancellationToken cancellationToken = default)
        {
            await SendLineRequestAsync(PushEndpoint, new
            {
                to,
                messages = new[] { new { type = "text", text = TrimLineText(text) } }
            }, cancellationToken);
        }

        private async Task PushMessageAsync(string to, object message, CancellationToken cancellationToken = default)
        {
            await SendLineRequestAsync(PushEndpoint, new
            {
                to,
                messages = new[] { message }
            }, cancellationToken, throwOnFailure: true);
        }

        public async Task ReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(replyToken))
                return;

            await SendLineRequestAsync(ReplyEndpoint, new
            {
                replyToken,
                messages = new[] { new { type = "text", text = TrimLineText(text) } }
            }, cancellationToken);
        }

        private async Task SendLineRequestAsync(
            string endpoint,
            object payload,
            CancellationToken cancellationToken,
            bool throwOnFailure = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _channelAccessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("LINE API request failed. Status={StatusCode}, Body={Body}", response.StatusCode, body);
                if (throwOnFailure)
                    throw new InvalidOperationException($"LINE API request failed. Status={(int)response.StatusCode}, Body={body}");
            }
        }

        private string BuildNotificationText(string title, string? message, string? targetUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);

            if (!string.IsNullOrWhiteSpace(message))
                sb.AppendLine(message.Trim());

            var absoluteUrl = ToAbsoluteUrl(targetUrl);
            if (!string.IsNullOrWhiteSpace(absoluteUrl))
                sb.AppendLine(absoluteUrl);

            return sb.ToString().Trim();
        }

        private object BuildNotificationFlexMessage(string title, string? message, string? absoluteUrl)
        {
            var bodyContents = new List<object>
            {
                new
                {
                    type = "text",
                    text = string.IsNullOrWhiteSpace(title) ? "แจ้งเตือนงาน" : title.Trim(),
                    weight = "bold",
                    size = "lg",
                    color = NotificationTitleColor(title),
                    wrap = true
                }
            };

            if (!string.IsNullOrWhiteSpace(message))
            {
                bodyContents.Add(new { type = "separator", margin = "md" });
                bodyContents.Add(new
                {
                    type = "text",
                    text = TrimFlexText(message.Trim(), 1800),
                    size = "sm",
                    color = "#334155",
                    wrap = true,
                    margin = "md"
                });
            }

            var bubbleContents = new List<object>
            {
                new
                {
                    type = "box",
                    layout = "vertical",
                    spacing = "sm",
                    contents = bodyContents
                }
            };

            if (!string.IsNullOrWhiteSpace(absoluteUrl))
            {
                bubbleContents.Add(new { type = "separator", margin = "md" });
                bubbleContents.Add(new
                {
                    type = "text",
                    text = $"ลิงก์รายละเอียด: {absoluteUrl}",
                    size = "xs",
                    color = "#2563EB",
                    wrap = true,
                    margin = "md",
                    action = new
                    {
                        type = "uri",
                        uri = absoluteUrl
                    }
                });
                bubbleContents.Add(new
                {
                    type = "button",
                    style = "primary",
                    color = NotificationActionColor(title),
                    height = "sm",
                    margin = "md",
                    action = new
                    {
                        type = "uri",
                        label = "เปิดรายละเอียด",
                        uri = absoluteUrl
                    }
                });
            }

            var body = new Dictionary<string, object?>
            {
                ["type"] = "box",
                ["layout"] = "vertical",
                ["paddingAll"] = "16px",
                ["backgroundColor"] = NotificationBackgroundColor(title),
                ["borderColor"] = NotificationBorderColor(title),
                ["borderWidth"] = "1px",
                ["cornerRadius"] = "12px",
                ["contents"] = bubbleContents
            };

            if (!string.IsNullOrWhiteSpace(absoluteUrl))
            {
                body["action"] = new
                {
                    type = "uri",
                    uri = absoluteUrl
                };
            }

            return new
            {
                type = "flex",
                altText = TrimLineText(BuildNotificationText(title, message, absoluteUrl), 400),
                contents = new
                {
                    type = "bubble",
                    size = "mega",
                    body
                }
            };
        }

        private static string NotificationTitleColor(string title)
        {
            var normalized = (title ?? "").Trim();
            if (normalized.StartsWith("ยกเลิกประชุม", StringComparison.OrdinalIgnoreCase))
                return "#DC2626";

            if (normalized.StartsWith("แจ้งเตือนประชุม", StringComparison.OrdinalIgnoreCase))
                return "#2563EB";

            if (normalized.StartsWith("งานล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#DC2626";

            if (normalized.StartsWith("งานเสี่ยงล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#D97706";

            return "#0F172A";
        }

        private static string NotificationBackgroundColor(string title)
        {
            var normalized = (title ?? "").Trim();
            if (normalized.StartsWith("ยกเลิกประชุม", StringComparison.OrdinalIgnoreCase))
                return "#FEF2F2";

            if (normalized.StartsWith("แจ้งเตือนประชุม", StringComparison.OrdinalIgnoreCase))
                return "#EFF6FF";

            if (normalized.StartsWith("งานล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#FEF2F2";

            if (normalized.StartsWith("งานเสี่ยงล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#FFFBEB";

            return "#F8FAFC";
        }

        private static string NotificationBorderColor(string title)
        {
            var normalized = (title ?? "").Trim();
            if (normalized.StartsWith("ยกเลิกประชุม", StringComparison.OrdinalIgnoreCase))
                return "#FECACA";

            if (normalized.StartsWith("แจ้งเตือนประชุม", StringComparison.OrdinalIgnoreCase))
                return "#BFDBFE";

            if (normalized.StartsWith("งานล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#FECACA";

            if (normalized.StartsWith("งานเสี่ยงล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#FDE68A";

            return "#E2E8F0";
        }

        private static string NotificationActionColor(string title)
        {
            var normalized = (title ?? "").Trim();
            if (normalized.StartsWith("ยกเลิกประชุม", StringComparison.OrdinalIgnoreCase))
                return "#DC2626";

            if (normalized.StartsWith("แจ้งเตือนประชุม", StringComparison.OrdinalIgnoreCase))
                return "#2563EB";

            if (normalized.StartsWith("งานล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#DC2626";

            if (normalized.StartsWith("งานเสี่ยงล่าช้า", StringComparison.OrdinalIgnoreCase))
                return "#D97706";

            return "#0F766E";
        }

        private string? ToAbsoluteUrl(string? targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return null;

            if (Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
                return targetUrl;

            if (string.IsNullOrWhiteSpace(_appBaseUrl))
                return null;

            return targetUrl.StartsWith("/")
                ? $"{_appBaseUrl}{targetUrl}"
                : $"{_appBaseUrl}/{targetUrl}";
        }

        private static string TrimLineText(string text, int maxLength = 5000)
            => string.IsNullOrWhiteSpace(text)
                ? "-"
                : (text.Length <= maxLength ? text : text[..maxLength]);

        private static string TrimFlexText(string text, int maxLength)
            => string.IsNullOrWhiteSpace(text)
                ? "-"
                : (text.Length <= maxLength ? text : text[..maxLength]);

        private static string Fingerprint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(hash)[..12];
        }
    }
}
