using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using ProjectTracking.Attributes;
using System.Linq;

namespace ProjectTracking.Controllers
{
    public class BaseController : Controller
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;

            // 🔐 ตรวจสอบ Login
            var userId = httpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                context.Result = new RedirectToActionResult("Login", "Auth", null);
                return;
            }

            // 🔐 ตรวจสอบ Permission
            var actionDescriptor = context.ActionDescriptor;

            var requireMenuAttr = actionDescriptor.EndpointMetadata
                .OfType<RequireMenuAttribute>()
                .FirstOrDefault();

            if (requireMenuAttr != null)
            {
                var menus = httpContext.Session.GetString("Menus");

                if (string.IsNullOrEmpty(menus) || !menus.Split(',').Contains(requireMenuAttr.MenuKey))
                {
                    context.Result = new RedirectToActionResult("AccessDenied", "Auth", null);
                    return;
                }
            }

            base.OnActionExecuting(context);
        }
    }
}