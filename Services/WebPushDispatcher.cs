using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using WebPush;
namespace ProjectTracking.Services;

public class WebPushDispatcher(IServiceScopeFactory scopes, IHttpClientFactory http, ILogger<WebPushDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Web push queue processing failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
    private async Task DispatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var service = scope.ServiceProvider.GetRequiredService<WebNotificationService>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var ids = await db.WebPushDeliveries.AsNoTracking().Where(d => !d.Done && d.NextAttemptAt <= now).OrderBy(d => d.Id).Take(20).Select(d => d.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            // Atomic lease prevents multiple server instances from dispatching the same job together.
            var claimed = await db.WebPushDeliveries.Where(d => d.Id == id && !d.Done && d.NextAttemptAt <= now).ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptAt, now.AddMinutes(2)).SetProperty(d => d.Attempts, d => d.Attempts + 1), ct);
            if (claimed == 0) continue;
            var delivery = await db.WebPushDeliveries.SingleAsync(d => d.Id == id, ct);
            var subscription = await db.WebPushSubscriptions.SingleOrDefaultAsync(s => s.Id == delivery.SubscriptionId && s.Active, ct);
            var notification = await db.WebNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == delivery.NotificationId, ct);
            if (subscription == null || notification == null || subscription.UserId != notification.UserId || !await db.LoginUsers.AnyAsync(u => u.UserId == notification.UserId && u.Status == "ACTIVE", ct)) { delivery.Done = true; await db.SaveChangesAsync(ct); continue; }
            try
            {
                using var json = JsonDocument.Parse(service.Protector.Unprotect(subscription.ProtectedSubscription));
                var root = json.RootElement; var endpoint = root.GetProperty("endpoint").GetString()!;
                if (!WebNotificationService.AllowedEndpoint(endpoint)) throw new InvalidOperationException("Unsupported push provider");
                var keys = root.GetProperty("keys");
                var target = new PushSubscription(endpoint, keys.GetProperty("p256dh").GetString(), keys.GetProperty("auth").GetString());
                var vapid = await service.KeysAsync(ct);
                // Keep personal task details off shared lock screens; opening the link requires the owning account.
                var payload = JsonSerializer.Serialize(new { title = "ProjectTracking", body = "คุณมีแจ้งเตือนงานใหม่ เปิดแอปเพื่อดูรายละเอียด", tag = $"work-{notification.Id}", url = $"/WorkNotifications/Open/{notification.Id}" });
                using var httpClient = http.CreateClient("WebPush");
                var client = new WebPushClient(httpClient);
                await client.SendNotificationAsync(target, payload, new Dictionary<string, object> { { "vapidDetails", vapid }, { "TTL", 86400 } }, ct);
                delivery.Done = true;
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            { subscription.Active = false; delivery.Done = true; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                delivery.Done = delivery.Attempts >= 4;
                delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(30 * Math.Pow(4, Math.Min(delivery.Attempts - 1, 3)));
                logger.LogWarning("Web push delivery {DeliveryId} failed on attempt {Attempt}; error type {ErrorType}", id, delivery.Attempts, ex.GetType().Name);
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }
}
