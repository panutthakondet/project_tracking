using Microsoft.AspNetCore.Mvc;

namespace ProjectTracking.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            // ===============================
            // ส่งข้อมูลที่จำเป็นให้ View
            // ===============================
            ViewBag.Username = HttpContext.Session.GetString("Username") ?? "-";

            // ✅ สำคัญ: ไม่ส่ง Model
            return View();
        }

        // ===============================
        // Logout
        // ===============================
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Auth");
        }
    }
}