using System.Collections.Generic;
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
                "UserManagement",
                "Meetings"
            };

        // ✅ บังคับสิทธิ์เมนูเฉพาะบาง action (ถ้าใน session มีรายการเมนู)
        // key = "Controller.Action" เช่น "Projects.ViewOnly"
        private static readonly Dictionary<(string Controller, string Action), string> ProtectedActionsToMenuKey =
            new Dictionary<(string, string), string>(new ControllerActionComparer())
            {
                { ("Projects", "ViewOnly"), "Projects.ViewOnly" },
                // เพิ่มรายการอื่น ๆ ได้ภายหลัง เช่น:
                // { ("ProjectIssues", "ViewOnly"), "ProjectIssues.ViewOnly" },
            };

        private sealed class ControllerActionComparer : IEqualityComparer<(string Controller, string Action)>
        {
            public bool Equals((string Controller, string Action) x, (string Controller, string Action) y)
                => string.Equals(x.Controller, y.Controller, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Action, y.Action, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string Controller, string Action) obj)
                => HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Controller ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Action ?? string.Empty));
        }

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

            // 5) ✅ ตรวจสิทธิ์เมนู (เฉพาะ action ที่กำหนด) — ถ้า session มีรายการเมนู
            var action = GetSecondSegment(path);
            if (string.IsNullOrWhiteSpace(action)) action = "Index";

            if (ProtectedActionsToMenuKey.TryGetValue((controller, action), out var menuKey))
            {
                // ถ้าไม่มีเมนูใน session เราจะไม่บล็อก เพื่อไม่กระทบระบบเดิม
                // แต่ถ้ามีเมนูแล้ว และไม่มีสิทธิ์ -> บล็อก
                if (HasMenuList(context) && !HasMenuKey(context, menuKey))
                {
                    context.Response.Redirect("/Home?forbidden=1");
                    return;
                }
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

        private static string GetSecondSegment(string path)
        {
            // /Controller/Action/...
            if (string.IsNullOrWhiteSpace(path) || path == "/") return "Index";

            var trimmed = path.Trim('/');
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "Index";
            return parts[1];
        }

        private static bool HasMenuList(HttpContext context)
        {
            // รองรับหลายชื่อ key เผื่อระบบเดิม
            return !string.IsNullOrWhiteSpace(context.Session.GetString("Menus"))
                   || !string.IsNullOrWhiteSpace(context.Session.GetString("MenuKeys"));
        }

        private static bool HasMenuKey(HttpContext context, string key)
        {
            // เมนูอาจถูกเก็บเป็น CSV/semicolon/pipe หรือ JSON string — เราเช็คแบบปลอดภัย
            var raw = context.Session.GetString("Menus") ?? context.Session.GetString("MenuKeys");
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // normalize
            raw = raw.Replace("\r", "").Replace("\n", "");

            // แยกแบบง่าย ๆ (รองรับ , ; |)
            var parts = raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (string.Equals(p.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // fallback: contains (กรณีเป็น JSON)
            return raw.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
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