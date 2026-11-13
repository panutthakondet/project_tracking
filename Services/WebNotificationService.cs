using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using WebPush;
namespace ProjectTracking.Services;

public class WebNotificationService(IDbContextFactory<AppDbContext> factory, IDataProtectionProvider protection, IConfiguration configuration, ILogger<WebNotificationService> logger)
{
    public IDataProtector Protector => protection.CreateProtector("ProjectTracking.WebPush.v1");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string Setting(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name) ?? configuration[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
    public string ApplicationBaseUrl => Setting("APP_BASE_URL", "https://tracking.soatservices.com");
    public string Subject => Setting("WEB_PUSH_SUBJECT", ApplicationBaseUrl);
    public async Task<VapidDetails> KeysAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var key = await db.WebPushKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Id == 1, ct);
        if (key == null)
        {
            var generated = VapidHelper.GenerateVapidKeys();
            var secret = Protector.Protect(generated.PrivateKey);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT IGNORE INTO web_push_keys (Id,PublicKey,ProtectedPrivateKey) VALUES (1,{generated.PublicKey},{secret})", ct);
            key = await db.WebPushKeys.AsNoTracking().SingleAsync(k => k.Id == 1, ct);
        }
        var subject = Subject.Trim(); if (!subject.Contains("://") && !subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) subject = "https://" + subject;
        return new VapidDetails(subject, key.PublicKey, Protector.Unprotect(key.ProtectedPrivateKey));
    }
    public string LocalTarget(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Contains('\\') || url.Any(char.IsControl)) return "/";
        if (url.StartsWith('/') && !url.StartsWith("//")) return url.Length <= 1000 ? url : "/";
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && Uri.TryCreate(ApplicationBaseUrl.Contains("://") ? ApplicationBaseUrl : "https://" + ApplicationBaseUrl, UriKind.Absolute, out var app) && absolute.Scheme == app.Scheme && absolute.Authority == app.Authority) return absolute.PathAndQuery.Length <= 1000 ? absolute.PathAndQuery : "/";
        return "/";
    }
    public static bool AllowedEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
        var host = uri.DnsSafeHost.ToLowerInvariant();
        return host == "fcm.googleapis.com" || host == "web.push.apple.com" || host == "push.services.mozilla.com" || host.EndsWith(".push.services.mozilla.com", StringComparison.Ordinal) || host.EndsWith(".notify.windows.com", StringComparison.Ordinal) || host.EndsWith(".notify.live.net", StringComparison.Ordinal);
    }
    // Isolated from business transactions: failure in this additional channel cannot cancel the original work.
    public async Task<int> NotifyEmployeeSafelyAsync(int empId, string title, string? message, string? target, CancellationToken ct = default, bool deduplicateDaily = false)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var users = await db.LoginUsers.AsNoTracking().Where(u => u.Status == "ACTIVE" && (u.EmpId == empId || (u.EmpId == null && db.Employees.Any(e => e.EmpId == empId && e.LoginUserId == u.UserId)))).Select(u => u.UserId).ToListAsync(ct);
            if (users.Count == 0) return 0;
            var now = DateTime.UtcNow; var count = 0; var text = (message ?? ""); if (text.Length > 16000) text = text[..16000]; title = title.Length > 255 ? title[..255] : title;
            var url = LocalTarget(target);
            foreach (var userId in users)
            {
                var bucket = deduplicateDaily ? "day:" + now.AddHours(7).ToString("yyyy-MM-dd") : (now.Ticks / TimeSpan.FromMinutes(5).Ticks).ToString();
                var dedup = Hash($"{userId}\n{title}\n{text}\n{url}\n{bucket}");
                if (await db.WebNotifications.AnyAsync(n => n.DedupKey == dedup, ct)) continue;
                var notification = new WebNotification { UserId = userId, EmpId = empId, Title = title, Message = text, TargetUrl = url, DedupKey = dedup, CreatedAt = now };
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                db.WebNotifications.Add(notification);
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { await transaction.RollbackAsync(ct); await transaction.DisposeAsync(); db.ChangeTracker.Clear(); if (await db.WebNotifications.AnyAsync(n => n.DedupKey == dedup, ct)) continue; throw; }
                var subscriptions = await db.WebPushSubscriptions.AsNoTracking().Where(s => s.UserId == userId && s.Active).Select(s => s.Id).ToListAsync(ct);
                db.WebPushDeliveries.AddRange(subscriptions.Select(id => new WebPushDelivery { NotificationId = notification.Id, SubscriptionId = id, NextAttemptAt = now }));
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); count++;
            }
            return count;
        }
        catch (Exception ex) { logger.LogWarning(ex, "Additional web notification failed for employee {EmpId}", empId); return 0; }
    }
    public async Task RevokeSessionAsync(int userId, string sessionId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var hash = Hash(sessionId);
        await db.WebPushSubscriptions.Where(s => s.UserId == userId && s.SessionHash == hash).ExecuteUpdateAsync(s => s.SetProperty(p => p.Active, false), ct);
    }
}
