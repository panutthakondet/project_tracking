using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Attributes;

namespace ProjectTracking.Attributes
{
    public class RequireMenuAttribute : Attribute
    {
        public string MenuKey { get; }

        public RequireMenuAttribute(string menuKey)
        {
            MenuKey = menuKey;
        }
    }
}

namespace ProjectTracking.Controllers
{
    public class SupportOrdersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

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
        [RequireMenu("SupportOrders.Create")]
        public async Task<IActionResult> Create(ProjectSupportOrder order, List<IFormFile> files)
        {
            ViewBag.EmployeeList = new SelectList(_context.Employees, "EmpId", "EmpName", order.AssignTo);

            if (!ModelState.IsValid)
                return View(order);

            // validate project id (prevent foreign key error)
            if (order.ProjectId <= 0)
            {
                ModelState.AddModelError("ProjectId", "Project is required.");
                return View(order);
            }

            var projectExists = await _context.Projects.AnyAsync(p => p.ProjectId == order.ProjectId);
            if (!projectExists)
            {
                ModelState.AddModelError("ProjectId", "Selected project does not exist.");
                return View(order);
            }

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
        [RequireMenu("SupportOrders.Edit")]
        public async Task<IActionResult> Edit(int id, ProjectSupportOrder order, List<IFormFile> files, List<IFormFile> afterFiles, List<int> deleteImageIds)
        {
            if (id != order.OrderId)
                return NotFound();

            if (!ModelState.IsValid)
                return View(order);

            // ===== Programmer Status Logic =====
            // If programmer uploads AFTER images -> mark FIXED
            if (afterFiles != null && afterFiles.Count > 0)
            {
                order.DevStatus = "FIXED";
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

            _context.ProjectSupportOrders.Remove(order);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }
}