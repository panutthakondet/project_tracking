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

        public bool IsWebhookSignatureValid(string body, string? signature)
        {
            if (string.IsNullOrWhiteSpace(_channelSecret) || string.IsNullOrWhiteSpace(signature))
                return false;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_channelSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            var expected = Convert.ToBase64String(hash);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature));
        }

        public async Task SendNotificationToEmployeeAsync(
            int empId,
            string title,
            string? message,
            string? targetUrl,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return;

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
                return;

            var text = BuildNotificationText(title, message, targetUrl);
            foreach (var lineUserId in lineUserIds)
            {
                await PushTextAsync(lineUserId, text, cancellationToken);
            }
        }

        public async Task PushTextAsync(string to, string text, CancellationToken cancellationToken = default)
        {
            await SendLineRequestAsync(PushEndpoint, new
            {
                to,
                messages = new[] { new { type = "text", text = TrimLineText(text) } }
            }, cancellationToken);
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

        private async Task SendLineRequestAsync(string endpoint, object payload, CancellationToken cancellationToken)
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

        private static string TrimLineText(string text)
            => string.IsNullOrWhiteSpace(text)
                ? "-"
                : (text.Length <= 5000 ? text : text[..5000]);
    }
}
