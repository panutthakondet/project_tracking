using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;

namespace ProjectTracking.Controllers
{
    public class BaseController : Controller
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // 🔐 ตรวจสอบการ Login
            var userId = context.HttpContext.Session.GetInt32("UserId");

            if (userId == null)
            {
                // ❌ ยังไม่ Login → เด้งไปหน้า Login
                context.Result = new RedirectToActionResult(
                    "Login",
                    "Auth",
                    null
                );
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}