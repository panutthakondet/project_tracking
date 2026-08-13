using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers;

public class ProjectTorItemsController : BaseController
{
    private readonly AppDbContext _context;
    public ProjectTorItemsController(AppDbContext context) => _context = context;

    [RequireMenu("PhaseAssigns.Create")]
    public async Task<IActionResult> Index(int projectId)
    {
        var project = await _context.Projects.AsNoTracking().Include(x => x.Coop)
            .FirstOrDefaultAsync(x => x.ProjectId == projectId);
        if (project == null) return NotFound();
        ViewBag.Project = project;
        return View(await _context.ProjectTorItems.AsNoTracking()
            .Where(x => x.ProjectId == projectId).OrderBy(x => x.SortOrder).ThenBy(x => x.TorItemId).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("PhaseAssigns.Create")]
    public async Task<IActionResult> Create(int projectId, string torCode, string title, string? detail, string? acceptanceCriteria)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "กรุณาระบุรายการ TOR";
            return RedirectToAction(nameof(Index), new { projectId });
        }
        var max = await _context.ProjectTorItems.Where(x => x.ProjectId == projectId).MaxAsync(x => (int?)x.SortOrder) ?? 0;
        _context.ProjectTorItems.Add(new ProjectTorItem
        {
            ProjectId = projectId, TorCode = torCode?.Trim() ?? string.Empty, Title = title.Trim(),
            Detail = detail?.Trim(), AcceptanceCriteria = acceptanceCriteria?.Trim(), SortOrder = max + 1,
            CreatedByEmpId = HttpContext.Session.GetInt32("EmpId")
        });
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { projectId });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("PhaseAssigns.Create")]
    public async Task<IActionResult> Toggle(int id)
    {
        var item = await _context.ProjectTorItems.FindAsync(id);
        if (item == null) return NotFound();
        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { projectId = item.ProjectId });
    }
}
