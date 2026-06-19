using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Services
{
    public sealed record TelegramAttachment(string FileName, string ContentType, byte[] Content);

    public class TelegramMessagingService
    {
        private const int MaxTelegramAttempts = 3;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _httpClient;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<TelegramMessagingService> _logger;
        private readonly string _botToken;
        private readonly string _webhookSecretToken;
        private readonly string _appBaseUrl;

        public TelegramMessagingService(
            HttpClient httpClient,
            IDbContextFactory<AppDbContext> dbFactory,
            IConfiguration configuration,
            ILogger<TelegramMessagingService> logger)
        {
            _httpClient = httpClient;
            _dbFactory = dbFactory;
            _logger = logger;
            _botToken = (Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                ?? configuration["TELEGRAM_BOT_TOKEN"]
                ?? "").Trim();
            _webhookSecretToken = (Environment.GetEnvironmentVariable("TELEGRAM_WEBHOOK_SECRET_TOKEN")
                ?? configuration["TELEGRAM_WEBHOOK_SECRET_TOKEN"]
                ?? "").Trim();
            _appBaseUrl = (Environment.GetEnvironmentVariable("APP_BASE_URL")
                ?? configuration["APP_BASE_URL"]
                ?? "").TrimEnd('/');
        }

        public bool IsConfigured
            => !string.IsNullOrWhiteSpace(_botToken);

        public bool HasWebhookSecretToken
            => !string.IsNullOrWhiteSpace(_webhookSecretToken);

        public bool HasAppBaseUrl
            => !string.IsNullOrWhiteSpace(_appBaseUrl);

        public string BotTokenFingerprint
            => Fingerprint(_botToken);

        public string WebhookSecretTokenFingerprint
            => Fingerprint(_webhookSecretToken);

        public bool IsWebhookSecretValid(string? headerValue)
        {
            if (string.IsNullOrWhiteSpace(_webhookSecretToken))
                return true;

            if (string.IsNullOrWhiteSpace(headerValue))
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(_webhookSecretToken),
                Encoding.UTF8.GetBytes(headerValue));
        }

        public async Task<int> SendNotificationToEmployeeAsync(
            int empId,
            string title,
            string? message,
            string? targetUrl,
            CancellationToken cancellationToken = default,
            TelegramAttachment? attachment = null)
        {
            if (!IsConfigured)
                return 0;

            var chatIds = await GetActiveTelegramChatIdsForEmployeeAsync(empId, cancellationToken);
            if (chatIds.Count == 0)
            {
                _logger.LogInformation(
                    "Telegram notification skipped because no active Telegram chat was linked to EmpId={EmpId}.",
                    empId);
                return 0;
            }

            return await SendNotificationToChatIdsAsync(
                chatIds,
                title,
                message,
                targetUrl,
                cancellationToken,
                attachment,
                empId);
        }

        public async Task<List<string>> GetActiveTelegramChatIdsForEmployeeAsync(
            int empId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.TelegramRecipients
                .AsNoTracking()
                .Where(x => x.IsActive
                    && x.EmpId == empId
                    && x.TelegramChatId != null
                    && x.TelegramChatId != "")
                .Select(x => x.TelegramChatId!)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        public async Task<int> SendNotificationToChatIdsAsync(
            IEnumerable<string> chatIds,
            string title,
            string? message,
            string? targetUrl,
            CancellationToken cancellationToken = default,
            TelegramAttachment? attachment = null,
            int? empIdForLog = null)
        {
            if (!IsConfigured)
                return 0;

            var targets = chatIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (targets.Count == 0)
                return 0;

            var absoluteUrl = ToAbsoluteUrl(targetUrl);
            if (!string.IsNullOrWhiteSpace(targetUrl) && string.IsNullOrWhiteSpace(absoluteUrl))
            {
                _logger.LogWarning(
                    "Telegram notification target URL could not be converted to absolute URL. TargetUrl={TargetUrl}, HasAppBaseUrl={HasAppBaseUrl}",
                    targetUrl,
                    HasAppBaseUrl);
            }

            var text = BuildNotificationText(title, message, absoluteUrl);
            var caption = BuildNotificationCaption(title, message, absoluteUrl);
            var sent = 0;
            var failed = 0;

            foreach (var chatId in targets)
            {
                try
                {
                    if (attachment != null)
                    {
                        await SendDocumentToChatAsync(
                            chatId,
                            attachment,
                            caption,
                            absoluteUrl,
                            cancellationToken,
                            throwOnFailure: true);
                    }
                    else
                    {
                        await SendMessageToChatAsync(chatId, text, absoluteUrl, cancellationToken, throwOnFailure: true);
                    }

                    await LogNotificationSendSuccessAsync(
                        "TELEGRAM",
                        empIdForLog,
                        chatId,
                        title,
                        message,
                        absoluteUrl,
                        cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Telegram notification failed for EmpId={EmpId}, ChatId={ChatId}",
                        empIdForLog,
                        MaskId(chatId));
                }
            }

            if (sent == 0 && failed > 0)
                throw new InvalidOperationException($"Telegram notification failed for all linked chats. EmpId={empIdForLog}, Failed={failed}");

            return sent;
        }

        public async Task SendTextToChatAsync(
            string chatId,
            string text,
            CancellationToken cancellationToken = default,
            bool throwOnFailure = false)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(chatId))
                return;

            await SendMessageToChatAsync(
                chatId,
                EscapeHtml(TrimTelegramText(text)),
                targetUrl: null,
                cancellationToken,
                throwOnFailure);
        }

        private async Task SendMessageToChatAsync(
            string chatId,
            string text,
            string? targetUrl,
            CancellationToken cancellationToken,
            bool throwOnFailure)
        {
            var payload = new
            {
                chat_id = chatId,
                text = TrimTelegramText(text),
                parse_mode = "HTML",
                disable_web_page_preview = true,
                reply_markup = string.IsNullOrWhiteSpace(targetUrl)
                    ? null
                    : new
                    {
                        inline_keyboard = new[]
                        {
                            new[]
                            {
                                new
                                {
                                    text = "เปิดรายละเอียด",
                                    url = targetUrl
                                }
                            }
                        }
                    }
            };

            await SendTelegramJsonRequestAsync("sendMessage", payload, cancellationToken, throwOnFailure);
        }

        private async Task SendDocumentToChatAsync(
            string chatId,
            TelegramAttachment attachment,
            string? caption,
            string? targetUrl,
            CancellationToken cancellationToken,
            bool throwOnFailure)
        {
            for (var attempt = 1; attempt <= MaxTelegramAttempts; attempt++)
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(chatId), "chat_id");

                if (!string.IsNullOrWhiteSpace(caption))
                    form.Add(new StringContent(TrimTelegramCaption(caption)), "caption");

                if (!string.IsNullOrWhiteSpace(targetUrl))
                {
                    var replyMarkup = new
                    {
                        inline_keyboard = new[]
                        {
                            new[]
                            {
                                new
                                {
                                    text = "เปิดรายละเอียด",
                                    url = targetUrl
                                }
                            }
                        }
                    };

                    form.Add(new StringContent(JsonSerializer.Serialize(replyMarkup, JsonOptions)), "reply_markup");
                }

                using var fileContent = new ByteArrayContent(attachment.Content);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(attachment.ContentType)
                        ? "application/octet-stream"
                        : attachment.ContentType);
                form.Add(fileContent, "document", string.IsNullOrWhiteSpace(attachment.FileName) ? "attachment.ics" : attachment.FileName);

                using var response = await _httpClient.PostAsync(TelegramEndpoint("sendDocument"), form, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (await TryDelayForTelegramRateLimitAsync(response, body, attempt, cancellationToken))
                    continue;

                _logger.LogWarning("Telegram document request failed. Status={StatusCode}, Body={Body}", response.StatusCode, body);
                if (throwOnFailure)
                    throw new InvalidOperationException($"Telegram document request failed. Status={(int)response.StatusCode}, Body={body}");

                return;
            }
        }

        private async Task SendTelegramJsonRequestAsync(
            string method,
            object payload,
            CancellationToken cancellationToken,
            bool throwOnFailure)
        {
            for (var attempt = 1; attempt <= MaxTelegramAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, TelegramEndpoint(method));
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOptions),
                    Encoding.UTF8,
                    "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (await TryDelayForTelegramRateLimitAsync(response, body, attempt, cancellationToken))
                    continue;

                _logger.LogWarning("Telegram API request failed. Method={Method}, Status={StatusCode}, Body={Body}", method, response.StatusCode, body);
                if (throwOnFailure)
                    throw new InvalidOperationException($"Telegram API request failed. Method={method}, Status={(int)response.StatusCode}, Body={body}");

                return;
            }
        }

        private async Task<bool> TryDelayForTelegramRateLimitAsync(
            HttpResponseMessage response,
            string body,
            int attempt,
            CancellationToken cancellationToken)
        {
            if ((int)response.StatusCode != 429 || attempt >= MaxTelegramAttempts)
                return false;

            var retryAfter = ReadTelegramRetryAfter(body);
            if (retryAfter <= 0)
                retryAfter = 1;

            _logger.LogWarning(
                "Telegram rate limit hit. Retrying after {RetryAfterSeconds}s. Attempt={Attempt}/{MaxAttempts}",
                retryAfter,
                attempt,
                MaxTelegramAttempts);

            await Task.Delay(TimeSpan.FromSeconds(retryAfter), cancellationToken);
            return true;
        }

        private static int ReadTelegramRetryAfter(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return 0;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("parameters", out var parameters)
                    && parameters.TryGetProperty("retry_after", out var retryAfter)
                    && retryAfter.TryGetInt32(out var seconds))
                {
                    return seconds;
                }
            }
            catch (JsonException)
            {
                return 0;
            }

            return 0;
        }

        private async Task LogNotificationSendSuccessAsync(
            string channel,
            int? recipientEmpId,
            string? recipientAddress,
            string title,
            string? message,
            string? targetUrl,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                db.NotificationSendLogs.Add(new NotificationSendLog
                {
                    Channel = channel,
                    RecipientEmpId = recipientEmpId,
                    RecipientAddress = TrimForLog(recipientAddress, 255),
                    Title = TrimForLog(title, 255) ?? "",
                    Message = message,
                    TargetUrl = TrimForLog(targetUrl, 500),
                    SentAt = DateTime.Now
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write notification send log. Channel={Channel}, EmpId={EmpId}", channel, recipientEmpId);
            }
        }

        private static string? TrimForLog(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private string TelegramEndpoint(string method)
            => $"https://api.telegram.org/bot{_botToken}/{method}";

        private string BuildNotificationText(string title, string? message, string? targetUrl)
        {
            var sb = new StringBuilder();
            sb.Append("<b>")
                .Append(EscapeHtml(string.IsNullOrWhiteSpace(title) ? "แจ้งเตือนงาน" : title.Trim()))
                .AppendLine("</b>");

            if (!string.IsNullOrWhiteSpace(message))
            {
                sb.AppendLine();
                sb.AppendLine(EscapeHtml(message.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                sb.AppendLine();
                sb.Append("ลิงก์รายละเอียด: ")
                    .Append(EscapeHtml(targetUrl));
            }

            return sb.ToString().Trim();
        }

        private static string BuildNotificationCaption(string title, string? message, string? targetUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(title) ? "แจ้งเตือนงาน" : title.Trim());

            if (!string.IsNullOrWhiteSpace(message))
            {
                sb.AppendLine();
                sb.AppendLine(message.Trim());
            }

            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                sb.AppendLine();
                sb.Append("ลิงก์รายละเอียด: ").Append(targetUrl);
            }

            return sb.ToString().Trim();
        }

        private string? ToAbsoluteUrl(string? targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return null;

            if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var absoluteUri)
                && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
            {
                return targetUrl;
            }

            if (string.IsNullOrWhiteSpace(_appBaseUrl))
                return null;

            var baseUrl = _appBaseUrl.Contains("://", StringComparison.Ordinal)
                ? _appBaseUrl
                : $"https://{_appBaseUrl}";

            var candidate = targetUrl.StartsWith("/")
                ? $"{baseUrl}{targetUrl}"
                : $"{baseUrl}/{targetUrl}";

            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    ? candidate
                    : null;
        }

        private static string TrimTelegramText(string text, int maxLength = 4096)
            => string.IsNullOrWhiteSpace(text)
                ? "-"
                : (text.Length <= maxLength ? text : text[..maxLength]);

        private static string TrimTelegramCaption(string text, int maxLength = 1024)
            => string.IsNullOrWhiteSpace(text)
                ? "-"
                : (text.Length <= maxLength ? text : text[..maxLength]);

        private static string EscapeHtml(string value)
            => System.Net.WebUtility.HtmlEncode(value ?? "");

        private static string Fingerprint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(hash)[..12];
        }

        private static string MaskId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value.Length <= 6 ? "***" : $"{value[..3]}...{value[^3..]}";
        }
    }
}
