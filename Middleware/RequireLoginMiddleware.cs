using Microsoft.AspNetCore.Http;

namespace ProjectTracking.Middleware
{
    public class RequireLoginMiddleware
    {
        private readonly RequestDelegate _next;

        // ✅ บังคับเฉพาะ controller ที่อยู่ในเมนูหน้า Home
        private static readonly HashSet<string> ProtectedControllers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Home",
                "Employees",
                "Projects",
                "ProjectPhases",
                "PhaseAssigns",
                "ProjectIssues",
                "PhaseStatusReport",
                "Dashboard",
                "IssueDashboard",
                "UserManagement"
            };

        public RequireLoginMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";

            // 1) Allow static files
            if (IsStaticFile(path))
            {
                await _next(context);
                return;
            }

            // 2) ✅ Allow Auth routes ALWAYS (รวมถึง /Auth/VerifyEmail ด้วย)
            // แต่ VerifyEmail จะไปบังคับ login ใน AuthController เอง (เพื่อให้ "ต้อง login ก่อนเสมอ")
            if (path.StartsWith("/Auth", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // 3) เช็ค controller segment แรก
            var controller = GetFirstSegment(path);

            // ถ้าไม่ใช่หน้าที่อยู่ในเมนู Home -> ไม่บังคับ
            if (string.IsNullOrWhiteSpace(controller) || !ProtectedControllers.Contains(controller))
            {
                await _next(context);
                return;
            }

            // 4) ถ้าเป็นหน้าที่ต้องบังคับ -> ต้องมี session
            var userId = context.Session.GetInt32("UserId");
            if (userId == null)
            {
                var returnUrl = context.Request.Path + context.Request.QueryString;
                var loginUrl = "/Auth/Login?returnUrl=" + Uri.EscapeDataString(returnUrl);

                context.Response.Redirect(loginUrl);
                return;
            }

            await _next(context);
        }

        private static string GetFirstSegment(string path)
        {
            // ✅ ให้ "/" ถือว่าเป็น Home เพื่อบังคับ login
            if (string.IsNullOrWhiteSpace(path) || path == "/") return "Home";

            var trimmed = path.Trim('/');
            var firstSlash = trimmed.IndexOf('/');
            return firstSlash >= 0 ? trimmed[..firstSlash] : trimmed;
        }

        private static bool IsStaticFile(string path)
        {
            if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/images", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)) return true;

            var ext = Path.GetExtension(path);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".css",".js",".png",".jpg",".jpeg",".gif",".svg",".webp",
                    ".ico",".map",".woff",".woff2",".ttf",".eot"
                };
                return allowed.Contains(ext);
            }

            return false;
        }
    }
}