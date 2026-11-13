using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MySqlConnector;
namespace ProjectTracking.Services;

public class WorkNotificationExceptionFilter(ILogger<WorkNotificationExceptionFilter> logger) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.HttpContext.RequestAborted.IsCancellationRequested) return;
        var trace = context.HttpContext.TraceIdentifier;
        var exceptions = new List<Exception>(); for (var ex = context.Exception; ex != null; ex = ex.InnerException) exceptions.Add(ex);
        var code = exceptions.Any(e => e is CryptographicException) ? "PUSH_KEY_UNAVAILABLE" : "NOTIFICATION_SERVICE_UNAVAILABLE";
        var title = code == "PUSH_KEY_UNAVAILABLE" ? "เซิร์ฟเวอร์อ่านกุญแจแจ้งเตือนไม่ได้ กรุณาให้ผู้ดูแลตรวจสอบ Data Protection keys" : "ระบบแจ้งเตือนบนเซิร์ฟเวอร์ยังไม่พร้อม กรุณาลองอีกครั้ง";
        var repaired = false;
        if (exceptions.OfType<MySqlException>().Any(e => e.Number == 1146 && new[] { "web_notifications", "web_push_subscriptions", "web_push_deliveries", "web_push_keys" }.Any(table => e.Message.Contains(table, StringComparison.OrdinalIgnoreCase))))
        {
            code = "NOTIFICATION_SCHEMA_UNAVAILABLE";
            title = "ฐานข้อมูลแจ้งเตือนยังไม่พร้อม กรุณาให้ผู้ดูแลตรวจสอบการ Deploy";
            try { await WebNotificationSchema.EnsureAsync(context.HttpContext.RequestServices); repaired = true; title = "เตรียมฐานข้อมูลแจ้งเตือนแล้ว กำลังลองใหม่"; }
            catch (Exception error) { logger.LogError(error, "Notification schema recovery failed. TraceId={TraceId}", trace); }
        }
        logger.LogError(context.Exception, "Notification API {Action} failed. Code={Code}, TraceId={TraceId}", context.RouteData.Values["action"], code, trace);
        var problem = new ProblemDetails { Status = 503, Title = title }; problem.Extensions["code"] = code; problem.Extensions["traceId"] = trace; problem.Extensions["recovered"] = repaired;
        context.HttpContext.Response.Headers["Cache-Control"] = "no-store";
        context.Result = new ObjectResult(problem) { StatusCode = 503 }; context.ExceptionHandled = true;
    }
}
