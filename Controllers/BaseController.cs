using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using ProjectTracking.Middleware;
using System;
using System.Linq;

namespace ProjectTracking.Controllers
{
    public class BaseController : Controller
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;
            var actionDescriptor = context.ActionDescriptor;

            if (actionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
            {
                base.OnActionExecuting(context);
                return;
            }

            // 🔐 ตรวจสอบ Login
            var userId = httpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                context.Result = new RedirectToActionResult("Login", "Auth", null);
                return;
            }

            // 🔐 ตรวจสอบ Permission
            var requireMenuAttr = actionDescriptor.EndpointMetadata
                .OfType<RequireMenuAttribute>()
                .FirstOrDefault();

            if (requireMenuAttr != null)
            {
                var role = (httpContext.Session.GetString("Role") ?? "").Trim();
                if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
                {
                    base.OnActionExecuting(context);
                    return;
                }

                var menus = httpContext.Session.GetString("Menus");

                var allowed = (menus ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim());

                if (string.IsNullOrWhiteSpace(menus) || !allowed.Contains(requireMenuAttr.Key, StringComparer.OrdinalIgnoreCase))
                {
                    context.Result = new RedirectToActionResult("AccessDenied", "Auth", new { key = requireMenuAttr.Key });
                    return;
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
