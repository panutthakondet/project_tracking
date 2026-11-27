using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.Services;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTracking.Controllers;

[RequireMenu("Projects.Index")]
public sealed class ProjectSyncController(AppDbContext db) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Index(int projectId, int? runId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (project == null) return NotFound();
        var runs = db.Set<ProjectSyncRun>().AsNoTracking().Where(run => run.ProjectId == projectId);
        ViewBag.ProjectName = project.ProjectName;
        ViewBag.ProjectId = projectId;
        ViewBag.History = await runs.OrderByDescending(run => run.Id).Take(100)
            .Select(run => new ProjectSyncRun { Id = run.Id, CreatedAtUtc = run.CreatedAtUtc, ComparedAtUtc = run.ComparedAtUtc })
            .ToListAsync(ct);
        if (runId == null) return View();
        var selected = await runs.FirstOrDefaultAsync(run => run.Id == runId, ct);
        return selected == null ? NotFound() : View(selected);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int projectId, IFormFile? maria, IFormFile? oracle, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(project => project.ProjectId == projectId, ct)) return NotFound();
        try
        {
            var draft = new SyncDraft(
                SyncComparison.ReadStructure(await Read(maria, ct), false),
                SyncComparison.ReadStructure(await Read(oracle, ct), true));
            var run = new ProjectSyncRun
            {
                ProjectId = projectId,
                CreatedAtUtc = DateTime.UtcNow,
                StructureJson = JsonSerializer.Serialize(draft)
            };
            db.Add(run);
            await db.SaveChangesAsync(ct);
            return RedirectToAction(nameof(Index), new { projectId, runId = run.Id });
        }
        catch (Exception ex) when (ex is FormatException or RegexMatchTimeoutException or ArgumentException)
        {
            TempData["SyncError"] = ex is FormatException ? ex.Message : "อ่านไฟล์โครงสร้างไม่ได้";
            return RedirectToAction(nameof(Index), new { projectId });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(int projectId, int runId, string operation,
        List<SyncPair> pairs, IFormFile? maria, IFormFile? oracle, CancellationToken ct)
    {
        var run = await db.Set<ProjectSyncRun>().FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == runId, ct);
        if (run == null) return NotFound();
        if (operation is not ("plan" or "oracle" or "mariadb" or "compare")) return BadRequest();
        try
        {
            if (!ModelState.IsValid) throw new FormatException("ข้อมูลที่ส่งมาไม่ถูกต้อง");
            var draft = JsonSerializer.Deserialize<SyncDraft>(run.StructureJson)!;
            var saved = run.ResultJson == null ? null : JsonSerializer.Deserialize<SyncResult>(run.ResultJson);
            var plan = run.PlanJson == null ? null : JsonSerializer.Deserialize<SyncPair[]>(run.PlanJson);
            if ((operation == "plan") != (plan == null))
                throw new FormatException("กรุณายืนยันคู่ตารางก่อนดาวน์โหลด SQL หรือนำเข้า CSV");
            var selected = plan ?? pairs.Where(pair => pair.Oracle >= 0).ToArray();
            var sql = SyncComparison.MakeSql(draft, selected);
            if (operation == "plan")
            {
                run.PlanJson = JsonSerializer.Serialize(selected);
                await db.SaveChangesAsync(ct);
            }
            else if (operation == "compare")
            {
                if (saved != null) throw new FormatException("รอบนี้บันทึกผลแล้ว");
                var metrics = SyncComparison.Compare(await Read(maria, ct), await Read(oracle, ct), sql.Maria);
                run.ResultJson = JsonSerializer.Serialize(new SyncResult(selected, metrics));
                run.ComparedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                return File(Encoding.UTF8.GetBytes(operation == "oracle" ? sql.Oracle : sql.Maria),
                    "application/octet-stream", $"sync-{runId}-{operation}.sql");
            }
        }
        catch (Exception ex) when (ex is FormatException or Microsoft.VisualBasic.FileIO.MalformedLineException or DbUpdateConcurrencyException)
        {
            TempData["SyncError"] = ex is FormatException ? ex.Message : "อ่าน CSV ไม่ได้ หรือรอบนี้ถูกบันทึกแล้ว";
        }
        return RedirectToAction(nameof(Index), new { projectId, runId });
    }

    private static async Task<string> Read(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 2097152)
            throw new FormatException("กรุณาเลือกไฟล์ขนาดไม่เกิน 2 MB");
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }
}
