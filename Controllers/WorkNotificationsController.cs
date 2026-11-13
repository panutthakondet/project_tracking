using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Services;
namespace ProjectTracking.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class WorkNotificationsController(AppDbContext db, WebNotificationService notifications) : Controller
{
    private async Task<int?> CurrentUser(CancellationToken ct)
    {
        var id = HttpContext.Session.GetInt32("UserId");
        return id.HasValue && await db.LoginUsers.AnyAsync(u => u.UserId == id.Value && u.Status == "ACTIVE", ct) ? id : null;
    }
    [HttpGet]
    public async Task<IActionResult> List(long? beforeId, CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        var rows = await db.WebNotifications.AsNoTracking().Where(n => n.UserId == user && (!beforeId.HasValue || n.Id < beforeId)).OrderByDescending(n => n.Id).Take(31).ToListAsync(ct);
        var unread = await db.WebNotifications.CountAsync(n => n.UserId == user && n.ReadAt == null, ct);
        return Json(new { unread, hasMore = rows.Count > 30, items = rows.Take(30).Select(n => new { n.Id, n.Title, n.Message, isRead = n.ReadAt.HasValue, createdAt = DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), url = Url.Action(nameof(Open), new { id = n.Id }) }) });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(long id, bool all = false, CancellationToken ct = default)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        var query = db.WebNotifications.Where(n => n.UserId == user && n.ReadAt == null && (all || n.Id == id));
        await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        return Ok();
    }
    [HttpGet]
    public async Task<IActionResult> Open(long id, CancellationToken ct)
    {
        var user = await CurrentUser(ct);
        if (user == null) return RedirectToAction("Login", "Auth", new { returnUrl = Url.Action(nameof(Open), new { id }) });
        var item = await db.WebNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id && n.UserId == user, ct);
        if (item == null) return NotFound();
        // A CSRF-protected handoff marks notification taps read without changing state on GET.
        return View("Open", item.Id);
    }
    [HttpPost, ValidateAntiForgeryToken, ActionName("Open")]
    public async Task<IActionResult> OpenConfirmed(long id, CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        var item = await db.WebNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id && n.UserId == user, ct);
        if (item == null) return NotFound();
        await db.WebNotifications.Where(n => n.Id == id && n.UserId == user && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        return LocalRedirect(notifications.LocalTarget(item.TargetUrl));
    }
    [HttpGet]
    public async Task<IActionResult> PushKey(CancellationToken ct)
    {
        if (await CurrentUser(ct) == null) return Unauthorized();
        var key = await notifications.KeysAsync(ct); return Json(new { publicKey = key.PublicKey });
    }
    public sealed class SubscriptionRequest
    {
        [Required, MaxLength(4096)] public string Endpoint { get; set; } = "";
        [Required] public SubscriptionKeys Keys { get; set; } = new();
    }
    public sealed class SubscriptionKeys
    {
        [Required, MaxLength(100)] public string P256dh { get; set; } = "";
        [Required, MaxLength(100)] public string Auth { get; set; } = "";
    }
    private static bool ValidKey(string value, int length)
    {
        try { var normalized = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')).Length == length; } catch (FormatException) { return false; }
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(8192)]
    public async Task<IActionResult> Subscribe([FromBody] SubscriptionRequest request, CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized();
        if (!ModelState.IsValid || !WebNotificationService.AllowedEndpoint(request.Endpoint) || !ValidKey(request.Keys.P256dh, 65) || !ValidKey(request.Keys.Auth, 16)) return BadRequest("ข้อมูลอุปกรณ์แจ้งเตือนไม่ถูกต้อง");
        var hash = WebNotificationService.Hash(request.Endpoint);
        if (await db.WebPushSubscriptions.CountAsync(s => s.UserId == user && s.Active && s.EndpointHash != hash, ct) >= 20) return BadRequest("เปิดแจ้งเตือนได้สูงสุด 20 อุปกรณ์");
        var protectedJson = notifications.Protector.Protect(JsonSerializer.Serialize(new { endpoint = request.Endpoint, keys = new { p256dh = request.Keys.P256dh, auth = request.Keys.Auth } }));
        var sessionHash = WebNotificationService.Hash(HttpContext.Session.Id); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO web_push_subscriptions (UserId,EndpointHash,SessionHash,ProtectedSubscription,Active,UpdatedAt) VALUES ({user.Value},{hash},{sessionHash},{protectedJson},1,{now}) ON DUPLICATE KEY UPDATE UserId={user.Value},SessionHash={sessionHash},ProtectedSubscription={protectedJson},Active=1,UpdatedAt={now}", ct);
        return Ok();
    }
    public sealed class EndpointRequest { [Required, MaxLength(4096)] public string Endpoint { get; set; } = ""; }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(8192)]
    public async Task<IActionResult> Unsubscribe([FromBody] EndpointRequest request, CancellationToken ct)
    {
        var user = await CurrentUser(ct); if (user == null) return Unauthorized(); if (!ModelState.IsValid) return BadRequest();
        var hash = WebNotificationService.Hash(request.Endpoint);
        await db.WebPushSubscriptions.Where(s => s.UserId == user && s.EndpointHash == hash).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, false), ct);
        return Ok();
    }
}
