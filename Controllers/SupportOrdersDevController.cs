using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Attributes;

namespace ProjectTracking.Controllers
{
    public class SupportOrdersDevController : Controller
    {
        private readonly AppDbContext _context;

        public SupportOrdersDevController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // Programmer Order List
        // =========================
        [RequireMenu("SupportOrdersDev.Index")]
        public async Task<IActionResult> Index(int? projectId)
        {
            var query = _context.ProjectSupportOrders
                .Include(o => o.Project)
                .Include(o => o.Employee)
                .AsQueryable();

            if (projectId.HasValue)
            {
                query = query.Where(o => o.ProjectId == projectId);
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.ProjectList = new SelectList(
                await _context.Projects.ToListAsync(),
                "ProjectId",
                "ProjectName",
                projectId
            );

            return View(orders);
        }

        // =========================
        // View Details
        // =========================
        [RequireMenu("SupportOrdersDev.Index")]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                .Include(o => o.Employee)
                .Include(o => o.FixImages)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            return View(order);
        }

        // =========================
        // Edit (Programmer Fix)
        // =========================
        [RequireMenu("SupportOrdersDev.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var order = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            order.FixImages = await _context.ProjectSupportFixImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            return View(order);
        }

        [HttpPost]
        [RequireMenu("SupportOrdersDev.Edit")]
        public async Task<IActionResult> Edit(int id, ProjectSupportOrder order, List<IFormFile> afterFiles, List<int> deleteImageIds)
        {
            var dbOrder = await _context.ProjectSupportOrders
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (dbOrder == null)
                return NotFound();

            // Use status selected by programmer
            dbOrder.DevStatus = order.DevStatus;

            var folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot/uploads/support",
                id.ToString()
            );

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            if (afterFiles != null && afterFiles.Count > 0)
            {
                foreach (var file in afterFiles)
                {
                    if (file.Length == 0) continue;

                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    _context.ProjectSupportFixImages.Add(new ProjectSupportFixImage
                    {
                        OrderId = id,
                        FilePath = $"/uploads/support/{id}/{fileName}",
                        ImageType = "AFTER"
                    });
                }
            }

            // Delete selected images (same style as BA)
            if (deleteImageIds != null && deleteImageIds.Any())
            {
                var images = await _context.ProjectSupportFixImages
                    .Where(x => deleteImageIds.Contains(x.ImageId))
                    .ToListAsync();

                foreach (var img in images)
                {
                    var filePath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        img.FilePath.TrimStart('/')
                    );

                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                }

                _context.ProjectSupportFixImages.RemoveRange(images);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = dbOrder.ProjectId });
        }
    }
}