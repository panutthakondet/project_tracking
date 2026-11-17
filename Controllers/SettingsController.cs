using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class SettingsController : BaseController
    {
        private readonly UserThemeService _themeService;

        public SettingsController(UserThemeService themeService)
        {
            _themeService = themeService;
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ColorVisionStyles(CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();
            return Json(await _themeService.GetColorVisionStylesAsync(userId.Value, cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ColorVision(string mode, CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();
            if (mode is not ("standard" or "red-green" or "blue-yellow" or "monochrome")) return BadRequest();
            await _themeService.SetColorVisionModeAsync(userId.Value, mode, cancellationToken);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return NoContent();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        [RequireMenu("Settings.Appearance")]
        public async Task<IActionResult> Appearance(CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Settings/Appearance" });

            var model = await _themeService.GetAppearanceAsync(userId.Value, cancellationToken);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Settings.Appearance")]
        public async Task<IActionResult> Appearance(AppearancePostViewModel model, CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Settings/Appearance" });

            var result = await _themeService.SaveAppearanceAsync(userId.Value, model, cancellationToken);
            if (!result.Success)
            {
                TempData["Error"] = result.Message;
                return RedirectToAction(nameof(Appearance));
            }

            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(Appearance));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Settings.Appearance")]
        public async Task<IActionResult> ResetAppearance(CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Settings/Appearance" });

            await _themeService.ResetAppearanceAsync(userId.Value, cancellationToken);
            TempData["Success"] = "กลับไปใช้ธีมเริ่มต้นเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Appearance));
        }
    }
}
