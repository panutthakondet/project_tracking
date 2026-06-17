using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class TelegramNotificationSettingsController : BaseController
    {
        private readonly TelegramNotificationSettingsService _settingsService;

        public TelegramNotificationSettingsController(TelegramNotificationSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        [RequireMenu("TelegramNotificationSettings.Index")]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var model = await _settingsService.BuildViewModelAsync(cancellationToken);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("TelegramNotificationSettings.Index")]
        public async Task<IActionResult> Save(
            LineNotificationSettingsPostViewModel model,
            CancellationToken cancellationToken)
        {
            await _settingsService.SaveAsync(model.Items, cancellationToken);
            TempData["Success"] = "บันทึกการตั้งค่า Telegram Notification แล้ว";
            return RedirectToAction(nameof(Index));
        }
    }
}
