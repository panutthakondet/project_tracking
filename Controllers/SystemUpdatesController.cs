using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class SystemUpdatesController : BaseController
    {
        private readonly AppDbContext _context;

        public SystemUpdatesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Acknowledge(List<int> updateIds, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Auth");

            var ids = (updateIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return RedirectSafely(returnUrl);

            var activeIds = await _context.SystemUpdateAnnouncements
                .AsNoTracking()
                .Where(x => ids.Contains(x.UpdateId) && x.IsActive)
                .Select(x => x.UpdateId)
                .ToListAsync();

            var existingIds = await _context.SystemUpdateReads
                .AsNoTracking()
                .Where(x => x.UserId == userId.Value && activeIds.Contains(x.UpdateId))
                .Select(x => x.UpdateId)
                .ToListAsync();

            var existingSet = existingIds.ToHashSet();
            foreach (var updateId in activeIds.Where(id => !existingSet.Contains(id)))
            {
                _context.SystemUpdateReads.Add(new SystemUpdateRead
                {
                    UpdateId = updateId,
                    UserId = userId.Value,
                    ReadAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            return RedirectSafely(returnUrl);
        }

        private IActionResult RedirectSafely(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }
    }
}
