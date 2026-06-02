using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class SupportOrdersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private static readonly string[] SupportOrderStatuses = { "OPEN", "WAIT_TEST", "DONE" };

        public SupportOrdersController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // =========================
        // LIST
        // =========================
        [RequireMenu("SupportOrders.Index")]
        public async Task<IActionResult> Index(int? projectId)
        {
            // send project list to dropdown
            ViewBag.ProjectList = new SelectList(_context.Projects, "ProjectId", "ProjectName", projectId);

            var query = _context.ProjectSupportOrders
                .Include(o => o.Project)
                .Include(o => o.Employee)
                .AsQueryable();

            // filter by selected project
            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(o => o.ProjectId == projectId.Value);
            }

            // send selected project name to view
            if (projectId.HasValue && projectId.Value > 0)
            {
                var project = await _context.Projects
                    .Where(p => p.ProjectId == projectId.Value)
                    .Select(p => p.ProjectName)
                    .FirstOrDefaultAsync();

                ViewBag.SelectedProjectName = project;
            }

            var orders = await query
                .OrderByDescending(o => o.OrderId)
                .ToListAsync();

            return View(orders);
        }

        [RequireMenu("SupportOrders.Index")]
        public async Task<IActionResult> ViewOnly(int? projectId, string? status, string? priority, string? devStatus)
        {
            var projects = await _context.Projects
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .ToListAsync();

            var query = _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(o => o.Project)
                .Include(o => o.Employee)
                .Include(o => o.FixImages)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(o => o.ProjectId == projectId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);

            if (!string.IsNullOrWhiteSpace(priority))
                query = query.Where(o => o.Priority == priority);

            if (!string.IsNullOrWhiteSpace(devStatus))
                query = query.Where(o => o.DevStatus == devStatus);

            var orders = await query
                .OrderBy(o => o.EndDate ?? DateTime.MaxValue)
                .ThenByDescending(o =>
                    o.Priority == "URGENT" ? 4 :
                    o.Priority == "HIGH" ? 3 :
                    o.Priority == "MEDIUM" ? 2 :
                    o.Priority == "LOW" ? 1 : 0)
                .ThenByDescending(o => o.CreatedAt)
                .ThenBy(o => o.OrderId)
                .ToListAsync();

            var orderIds = orders.Select(o => o.OrderId).ToList();
            var imageCounts = orderIds.Any()
                ? await _context.ProjectSupportImages
                    .AsNoTracking()
                    .Where(x => orderIds.Contains(x.OrderId))
                    .GroupBy(x => x.OrderId)
                    .ToDictionaryAsync(g => g.Key, g => g.Count())
                : new Dictionary<int, int>();

            ViewBag.Projects = projects;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedStatus = status ?? "";
            ViewBag.SelectedPriority = priority ?? "";
            ViewBag.SelectedDevStatus = devStatus ?? "";
            ViewBag.ImageCounts = imageCounts;
            ViewBag.StatusList = SupportOrderStatuses;
            ViewBag.PriorityList = new[] { "URGENT", "HIGH", "MEDIUM", "LOW" };
            ViewBag.DevStatusList = new[] { "IN_PROGRESS", "FIXED" };

            return View(orders);
        }

        // =========================
        // DETAIL
        // =========================
        [RequireMenu("SupportOrders.Index")]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                .Include(o => o.Employee)
                .Include(o => o.FixImages)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            // Load BA images
            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == order.OrderId)
                .ToListAsync();

            return View(order);
        }

        // =========================
        // CREATE (GET)
        // =========================
        [RequireMenu("SupportOrders.Create")]
        public IActionResult Create(int projectId)
        {
            ViewBag.EmployeeList = new SelectList(_context.Employees, "EmpId", "EmpName");

            if (projectId > 0)
            {
                ViewBag.SelectedProjectName = _context.Projects
                    .Where(p => p.ProjectId == projectId)
                    .Select(p => p.ProjectName)
                    .FirstOrDefault();
            }

            var model = new ProjectSupportOrder
            {
                ProjectId = projectId,
                Status = "OPEN",
                Priority = "MEDIUM"
            };

            return View(model);
        }

        // =========================
        // CREATE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SupportOrders.Create")]
        public async Task<IActionResult> Create(ProjectSupportOrder order, List<IFormFile> files)
        {
            ApplySupportDateInput(order);
            ValidateSupportDateRange(order, requireDates: true);
            await PopulateSupportOrderFormAsync(order);

            if (!ModelState.IsValid)
                return View(order);

            // validate project id (prevent foreign key error)
            if (order.ProjectId <= 0)
            {
                ModelState.AddModelError("ProjectId", "Project is required.");
                return View(order);
            }

            var projectBaEmpId = await _context.Projects
                .AsNoTracking()
                .Where(p => p.ProjectId == order.ProjectId)
                .Select(p => p.BaEmpId)
                .FirstOrDefaultAsync();

            var projectExists = await _context.Projects.AnyAsync(p => p.ProjectId == order.ProjectId);
            if (!projectExists)
            {
                ModelState.AddModelError("ProjectId", "Selected project does not exist.");
                return View(order);
            }

            order.CreatedBy = projectBaEmpId ?? await GetCurrentEntryIdAsync();

            order.CreatedAt = DateTime.Now;
            order.Status = NormalizeSupportStatus(order.Status);

            _context.ProjectSupportOrders.Add(order);
            await _context.SaveChangesAsync();

            // upload images
            if (files != null && files.Count > 0)
            {
                var folder = Path.Combine(_env.WebRootPath, "uploads/support", order.OrderId.ToString());

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                foreach (var file in files)
                {
                    if (file.Length == 0) continue;

                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    _context.ProjectSupportImages.Add(new ProjectSupportImage
                    {
                        OrderId = order.OrderId,
                        FileName = fileName,
                        FilePath = $"/uploads/support/{order.OrderId}/{fileName}"
                    });
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index", new { projectId = order.ProjectId });
        }

        // =========================
        // EDIT (GET)
        // =========================
        [RequireMenu("SupportOrders.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var order = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                .Include(o => o.Employee)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            // Load BA images
            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == order.OrderId)
                .ToListAsync();

            // Send project name to view
            ViewBag.SelectedProjectName = order.Project?.ProjectName;

            ViewBag.EmployeeList = new SelectList(_context.Employees, "EmpId", "EmpName", order.AssignTo);

            return View(order);
        }

        // =========================
        // EDIT (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SupportOrders.Edit")]
        public async Task<IActionResult> Edit(int id, ProjectSupportOrder order, List<IFormFile> files, List<IFormFile> afterFiles, List<int> deleteImageIds)
        {
            if (id != order.OrderId)
                return NotFound();

            ApplySupportDateInput(order);
            ValidateSupportDateRange(order);

            if (!ModelState.IsValid)
            {
                await PopulateSupportOrderFormAsync(order);
                return View(order);
            }

            // โหลดข้อมูลเดิมก่อน เพื่อกันค่า DevStatus หาย
            var existingOrder = await _context.ProjectSupportOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OrderId == order.OrderId);

            if (existingOrder == null)
                return NotFound();

            // ===== Programmer Status Logic =====
            // ถ้า programmer upload AFTER image -> FIXED
            if (afterFiles != null && afterFiles.Count > 0)
            {
                order.DevStatus = "FIXED";
            }
            else
            {
                // ถ้าไม่ได้ส่งค่า DevStatus จากหน้า form ให้คงค่าเดิมไว้
                order.DevStatus = existingOrder.DevStatus;
            }

            order.CreatedBy = existingOrder.CreatedBy;
            order.CreatedAt = DateTime.Now;
            order.Status = NormalizeSupportStatus(order.Status);
            if (IsDevStatusChangedToFixed(existingOrder.DevStatus, order.DevStatus))
            {
                order.Status = "WAIT_TEST";
            }

            _context.ProjectSupportOrders.Update(order);
            await _context.SaveChangesAsync();

            // ===== Delete BA images =====
            if (deleteImageIds != null && deleteImageIds.Count > 0)
            {
                var imagesToDelete = await _context.ProjectSupportImages
                    .Where(x => deleteImageIds.Contains(x.ImageId))
                    .ToListAsync();

                foreach (var img in imagesToDelete)
                {
                    var relativePath = (img.FilePath ?? "").TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString());
                    var physicalPath = Path.Combine(_env.WebRootPath, relativePath);

                    if (System.IO.File.Exists(physicalPath))
                    {
                        System.IO.File.Delete(physicalPath);
                    }

                    _context.ProjectSupportImages.Remove(img);
                }
            }

            var folder = Path.Combine(_env.WebRootPath, "uploads/support", order.OrderId.ToString());

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            // ===== Upload BA images =====
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    if (file.Length == 0) continue;

                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    _context.ProjectSupportImages.Add(new ProjectSupportImage
                    {
                        OrderId = order.OrderId,
                        FileName = fileName,
                        FilePath = $"/uploads/support/{order.OrderId}/{fileName}"
                    });
                }
            }

            // ===== Upload AFTER images =====
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
                        OrderId = order.OrderId,
                        FilePath = $"/uploads/support/{order.OrderId}/{fileName}",
                        ImageType = "AFTER"
                    });
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { projectId = order.ProjectId });
        }

        // =========================
        // DELETE
        // =========================
        [HttpPost]
        [RequireMenu("SupportOrders.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.ProjectSupportOrders
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            order.CreatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            _context.ProjectSupportOrders.Remove(order);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        private void ApplySupportDateInput(ProjectSupportOrder order)
        {
            ModelState.Remove(nameof(ProjectSupportOrder.StartDate));
            ModelState.Remove(nameof(ProjectSupportOrder.EndDate));

            var startRaw = Request.Form[nameof(ProjectSupportOrder.StartDate)].ToString();
            var endRaw = Request.Form[nameof(ProjectSupportOrder.EndDate)].ToString();

            order.StartDate = ParseSupportDate(startRaw);
            order.EndDate = ParseSupportDate(endRaw);

            if (!string.IsNullOrWhiteSpace(startRaw) && !order.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.StartDate), "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
            }

            if (!string.IsNullOrWhiteSpace(endRaw) && !order.EndDate.HasValue)
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.EndDate), "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
            }
        }

        private static DateTime? ParseSupportDate(string? value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return null;

            if (DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var isoDate))
            {
                return isoDate.Date;
            }

            var parts = value.Split('/');
            if (parts.Length == 3
                && int.TryParse(parts[0], out var day)
                && int.TryParse(parts[1], out var month)
                && int.TryParse(parts[2], out var year))
            {
                if (year > 2400) year -= 543;

                try
                {
                    return new DateTime(year, month, day);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private void ValidateSupportDateRange(ProjectSupportOrder order, bool requireDates = false)
        {
            if (requireDates && !order.StartDate.HasValue && !HasModelError(nameof(ProjectSupportOrder.StartDate)))
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.StartDate), "กรุณากรอกวันที่เริ่ม");
            }

            if (requireDates && !order.EndDate.HasValue && !HasModelError(nameof(ProjectSupportOrder.EndDate)))
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.EndDate), "กรุณากรอกวันที่สิ้นสุด");
            }

            if (order.StartDate.HasValue
                && order.EndDate.HasValue
                && order.EndDate.Value.Date < order.StartDate.Value.Date)
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.EndDate), "วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่ม");
            }
        }

        private bool HasModelError(string key)
        {
            return ModelState.TryGetValue(key, out var state) && state.Errors.Count > 0;
        }

        private async Task PopulateSupportOrderFormAsync(ProjectSupportOrder order)
        {
            ViewBag.EmployeeList = new SelectList(
                await _context.Employees
                    .AsNoTracking()
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                order.AssignTo);

            if (order.ProjectId > 0)
            {
                ViewBag.SelectedProjectName = await _context.Projects
                    .AsNoTracking()
                    .Where(p => p.ProjectId == order.ProjectId)
                    .Select(p => p.ProjectName)
                    .FirstOrDefaultAsync();
            }

            if (order.OrderId > 0)
            {
                order.Images = await _context.ProjectSupportImages
                    .AsNoTracking()
                    .Where(x => x.OrderId == order.OrderId)
                    .ToListAsync();
            }
        }

        private async Task<int?> GetCurrentEntryIdAsync()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue) return null;

            var empId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.LoginUserId == userId.Value)
                .Select(e => (int?)e.EmpId)
                .FirstOrDefaultAsync();

            if (empId.HasValue) return empId;

            return await _context.LoginUsers
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.EmpId)
                .FirstOrDefaultAsync();
        }

        private static string NormalizeSupportStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return SupportOrderStatuses.Contains(normalized) ? normalized : "OPEN";
        }

        private static bool IsDevStatusChangedToFixed(string? previousStatus, string? nextStatus)
        {
            return !string.Equals((previousStatus ?? "").Trim(), "FIXED", StringComparison.OrdinalIgnoreCase)
                && string.Equals((nextStatus ?? "").Trim(), "FIXED", StringComparison.OrdinalIgnoreCase);
        }
    }
}
