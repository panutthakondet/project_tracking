using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ProjectTracking.Middleware
{
    /// <summary>
    /// ใช้ตรวจสิทธิเมนูจาก Session("Menus")
    /// - ADMIN ผ่านทุกเมนู
    /// - USER ต้องมี key อยู่ใน Menus (คั่นด้วย ,)
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RequireMenuAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _menuKey;

        public RequireMenuAttribute(string menuKey)
        {
            _menuKey = (menuKey ?? "").Trim();
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // ถ้า key ว่าง -> ไม่บังคับ
            if (string.IsNullOrWhiteSpace(_menuKey)) return;

            var http = context.HttpContext;

            // ต้อง login ก่อน
            var userId = http.Session.GetInt32("UserId");
            if (userId == null)
            {
                var returnUrl = http.Request.Path + http.Request.QueryString;
                context.Result = new RedirectToActionResult("Login", "Auth", new { returnUrl = returnUrl.ToString() });
                return;
            }

            // ADMIN ผ่าน
            var role = (http.Session.GetString("Role") ?? "").Trim();
            if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)) return;

            // ตรวจ Menus
            var menuRaw = (http.Session.GetString("Menus") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(menuRaw))
            {
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return;
            }

            var allowed = menuRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim());

            if (!allowed.Contains(_menuKey, StringComparer.OrdinalIgnoreCase))
            {
                context.Result = new RedirectToActionResult("Index", "Home", null);
            }
        }
    }
}
