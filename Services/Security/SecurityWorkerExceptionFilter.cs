using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ProjectTracking.Services.Security;

// Worker requests must never fall through to the interactive Home/Error login route.
public sealed class SecurityWorkerExceptionFilter(ILogger<SecurityWorkerExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.HttpContext.RequestAborted.IsCancellationRequested) return;
        var trace = context.HttpContext.TraceIdentifier;
        logger.LogError(context.Exception,
            "Security worker API {Action} failed for scan {ScanId}. TraceId={TraceId}",
            context.RouteData.Values["action"], context.RouteData.Values["id"], trace);
        var problem = new ProblemDetails {
            Status = StatusCodes.Status500InternalServerError,
            Title = "บันทึกหรือประมวลผลงานสแกนไม่สำเร็จ กรุณาตรวจสอบ Server log ตามเลขอ้างอิง"
        };
        problem.Extensions["code"] = "SECURITY_WORKER_SERVER_ERROR";
        problem.Extensions["traceId"] = trace;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers["X-Security-Error"] = "SECURITY_WORKER_SERVER_ERROR";
        context.Result = new ObjectResult(problem) { StatusCode = 500 };
        context.ExceptionHandled = true;
    }
}
