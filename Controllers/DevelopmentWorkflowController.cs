using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers;

public class DevelopmentWorkflowController : BaseController
{
    private readonly AppDbContext _context;
    public DevelopmentWorkflowController(AppDbContext context) => _context = context;

    [RequireMenu("PhaseAssigns.Index")]
    public async Task<IActionResult> Index()
    {
        var empId = HttpContext.Session.GetInt32("EmpId");
        var admin = IsAdmin();
        var query = from a in _context.PhaseAssigns.AsNoTracking()
                    join p in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals p.PhaseId
                    join pr in _context.Projects.AsNoTracking() on p.ProjectId equals pr.ProjectId
                    join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                    where admin || a.EmpId == empId
                    orderby a.PlanEnd, a.AssignId descending
                    select new DevelopmentQueueRow { AssignId = a.AssignId, ProjectId = pr.ProjectId, ProjectName = pr.ProjectName,
                        PhaseName = p.PhaseDisplayName, WorkName = a.Role ?? p.PhaseName, EmployeeName = e.EmpName,
                        WorkflowStatus = a.WorkflowStatus };
        return View(await query.ToListAsync());
    }

    [RequireMenu("TestScenarios.Index")]
    public async Task<IActionResult> BaQueue()
    {
        var rows = await (from a in _context.PhaseAssigns.AsNoTracking()
                          join p in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals p.PhaseId
                          join pr in _context.Projects.AsNoTracking() on p.ProjectId equals pr.ProjectId
                          join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                          where a.WorkflowStatus == "READY_FOR_BA" || a.WorkflowStatus == "BA_TESTING"
                          orderby a.PlanEnd, a.AssignId
                          select new DevelopmentQueueRow { AssignId = a.AssignId, ProjectId = pr.ProjectId, ProjectName = pr.ProjectName,
                              PhaseName = p.PhaseDisplayName, WorkName = a.Role ?? p.PhaseName, EmployeeName = e.EmpName,
                              WorkflowStatus = a.WorkflowStatus }).ToListAsync();
        return View(rows);
    }

    [RequireMenu("PhaseAssigns.Index")]
    public async Task<IActionResult> Dev(int id)
    {
        var vm = await LoadAsync(id);
        if (vm == null) return NotFound();
        vm.CanWorkAsDev = CanOwnAssignment(vm.Assignment);
        if (!vm.CanWorkAsDev) return Forbid();
        return View(vm);
    }

    [RequireMenu("TestScenarios.Index")]
    public async Task<IActionResult> Verify(int id)
    {
        var vm = await LoadAsync(id);
        if (vm == null) return NotFound();
        if (!CanBaWork(vm.Assignment)) return BadRequest("งานนี้ยังไม่ถูกส่งให้ BA ทดสอบ");
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("PhaseAssigns.Index")]
    public async Task<IActionResult> SaveTor(int id, int torItemId, bool completed, string? remark)
    {
        var assign = await OwnedAssignmentAsync(id);
        if (assign == null) return Forbid();
        if (!CanDevWork(assign)) return BadRequest("งานถูกส่งให้ BA แล้ว ไม่สามารถแก้ผล Dev ได้");
        var row = await _context.PhaseAssignTorItems.FindAsync(id, torItemId);
        if (row == null) return NotFound();
        row.CheckStatus = completed ? "COMPLETED" : "PENDING";
        row.Remark = remark?.Trim();
        row.CheckedByEmpId = HttpContext.Session.GetInt32("EmpId");
        row.CheckedAt = completed ? DateTime.Now : null;
        assign.WorkflowStatus = "IN_DEVELOPMENT";
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Dev), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("PhaseAssigns.Index")]
    public async Task<IActionResult> SaveDevTest(int id, int scenarioId, string resultStatus, string? remark)
    {
        var assign = await OwnedAssignmentAsync(id);
        if (assign == null) return Forbid();
        if (!CanDevWork(assign)) return BadRequest("งานถูกส่งให้ BA แล้ว ไม่สามารถแก้ผล Dev ได้");
        if (!await IsLinkedScenarioAsync(id, scenarioId)) return BadRequest();
        await AddRunAsync(id, scenarioId, "DEV", resultStatus, remark);
        assign.WorkflowStatus = "DEV_TESTING";
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Dev), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("PhaseAssigns.Index")]
    public async Task<IActionResult> SubmitToBa(int id)
    {
        var assign = await OwnedAssignmentAsync(id);
        if (assign == null) return Forbid();
        if (!CanDevWork(assign)) return BadRequest("สถานะงานไม่พร้อมส่งให้ BA");
        var vm = await LoadAsync(id);
        if (vm == null) return NotFound();
        if (vm.TorItems.Count == 0 || vm.Scenarios.Count == 0 || vm.TorPercent < 100 || vm.DevPercent < 100)
        {
            TempData["Error"] = "ต้อง Checklist TOR และ Dev Test ผ่านครบทุกข้อก่อนส่งให้ BA";
            return RedirectToAction(nameof(Dev), new { id });
        }
        assign.WorkflowStatus = "READY_FOR_BA";
        var phaseProjectId = await _context.ProjectPhases.Where(x => x.PhaseId == assign.PhaseId).Select(x => x.ProjectId).FirstAsync();
        var baEmpId = await _context.Projects.Where(x => x.ProjectId == phaseProjectId).Select(x => x.BaEmpId).FirstOrDefaultAsync();
        if (baEmpId.HasValue)
            AddNotification(baEmpId.Value, id, "มีงานรอ BA ทดสอบ", vm.Assignment.Role, $"/DevelopmentWorkflow/Verify/{id}", "INFO");
        await _context.SaveChangesAsync();
        TempData["Success"] = "ส่งงานให้ BA ทดสอบแล้ว";
        return RedirectToAction(nameof(Dev), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("TestScenarios.Edit")]
    public async Task<IActionResult> SaveBaTest(int id, int scenarioId, string resultStatus, string? remark)
    {
        var assign = await _context.PhaseAssigns.FindAsync(id);
        if (assign == null) return NotFound();
        if (!CanBaWork(assign)) return BadRequest("งานนี้ยังไม่ถูกส่งให้ BA ทดสอบ");
        if (!await IsLinkedScenarioAsync(id, scenarioId)) return BadRequest();
        await AddRunAsync(id, scenarioId, "BA", resultStatus, remark);
        assign.WorkflowStatus = "BA_TESTING";
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Verify), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("TestScenarios.Edit")]
    public async Task<IActionResult> Complete(int id)
    {
        var assign = await _context.PhaseAssigns.FindAsync(id);
        var vm = await LoadAsync(id);
        if (assign == null || vm == null) return NotFound();
        if (!CanBaWork(assign)) return BadRequest("สถานะงานไม่พร้อมปิด");
        if (vm.Scenarios.Count == 0 || vm.BaPercent < 100)
        {
            TempData["Error"] = "BA ต้องทดสอบผ่านครบทุก Scenario ก่อนปิดงาน";
            return RedirectToAction(nameof(Verify), new { id });
        }
        assign.WorkflowStatus = "COMPLETED";
        assign.WorkStatus = "DONE";
        AddNotification(assign.EmpId, id, "BA ทดสอบผ่านครบแล้ว", assign.Role, $"/DevelopmentWorkflow/Dev/{id}", "SUCCESS");
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(BaQueue));
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("TestScenarios.Edit")]
    public async Task<IActionResult> ReturnToDev(int id)
    {
        var assign = await _context.PhaseAssigns.FindAsync(id);
        if (assign == null) return NotFound();
        if (!CanBaWork(assign)) return BadRequest("สถานะงานไม่พร้อมส่งกลับ");
        var latestBaRuns = (await _context.TestScenarioRuns.Where(x => x.AssignId == id && x.TestStage == "BA")
            .OrderByDescending(x => x.RoundNo).ThenByDescending(x => x.RunId).ToListAsync())
            .GroupBy(x => x.ScenarioId).Select(x => x.First())
            .Where(x => x.ResultStatus is "FAIL" or "BLOCKED").ToList();
        if (latestBaRuns.Count == 0)
        {
            TempData["Error"] = "ต้องบันทึก Scenario ที่ไม่ผ่านหรือติดปัญหาพร้อมหมายเหตุก่อนส่งกลับ Dev";
            return RedirectToAction(nameof(Verify), new { id });
        }
        foreach (var failed in latestBaRuns)
            await AddRunAsync(id, failed.ScenarioId, "DEV", "BLOCKED", $"BA ส่งกลับ: {failed.Remark}");
        assign.WorkflowStatus = "REWORK";
        assign.WorkStatus = "IN_PROGRESS";
        AddNotification(assign.EmpId, id, "BA ส่งงานกลับให้ Dev แก้ไข", string.Join(" | ", latestBaRuns.Select(x => x.Remark).Where(x => !string.IsNullOrWhiteSpace(x))), $"/DevelopmentWorkflow/Dev/{id}", "WARNING");
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(BaQueue));
    }

    private bool IsAdmin() => string.Equals(HttpContext.Session.GetString("Role"), "ADMIN", StringComparison.OrdinalIgnoreCase);
    private void AddNotification(int empId, int sourceId, string title, string? message, string targetUrl, string severity)
    {
        _context.UserNotifications.Add(new UserNotification
        {
            RecipientEmpId = empId, SourceType = "DEVELOPMENT_WORKFLOW", SourceId = sourceId,
            Title = title, Message = message, TargetUrl = targetUrl, Severity = severity,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
    }
    private async Task<PhaseAssign?> OwnedAssignmentAsync(int id)
    {
        var a = await _context.PhaseAssigns.FindAsync(id);
        return a != null && CanOwnAssignment(a) ? a : null;
    }
    private bool CanOwnAssignment(PhaseAssign assignment) =>
        IsAdmin() || assignment.EmpId == HttpContext.Session.GetInt32("EmpId");
    private static bool CanDevWork(PhaseAssign assignment) => assignment.WorkflowStatus is "IN_DEVELOPMENT" or "DEV_TESTING" or "REWORK";
    private static bool CanBaWork(PhaseAssign assignment) => assignment.WorkflowStatus is "READY_FOR_BA" or "BA_TESTING";
    private Task<bool> IsLinkedScenarioAsync(int id, int scenarioId) =>
        _context.PhaseAssignTestScenarios.AnyAsync(x => x.AssignId == id && x.ScenarioId == scenarioId);

    private async Task AddRunAsync(int id, int scenarioId, string stage, string status, string? remark)
    {
        status = (status ?? "").Trim().ToUpperInvariant();
        if (status is not ("PASS" or "FAIL" or "BLOCKED")) status = "BLOCKED";
        var round = (await _context.TestScenarioRuns.Where(x => x.AssignId == id && x.ScenarioId == scenarioId && x.TestStage == stage)
            .MaxAsync(x => (int?)x.RoundNo) ?? 0) + 1;
        _context.TestScenarioRuns.Add(new TestScenarioRun { AssignId = id, ScenarioId = scenarioId, TestStage = stage,
            RoundNo = round, ResultStatus = status, Remark = remark?.Trim(), TestedAt = DateTime.Now,
            TestedByEmpId = HttpContext.Session.GetInt32("EmpId") });
    }

    private async Task<DevelopmentWorkflowViewModel?> LoadAsync(int id)
    {
        var core = await (from a in _context.PhaseAssigns.AsNoTracking()
                          join p in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals p.PhaseId
                          join pr in _context.Projects.AsNoTracking().Include(x => x.Coop) on p.ProjectId equals pr.ProjectId
                          join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                          where a.AssignId == id select new { a, p, pr, e }).FirstOrDefaultAsync();
        if (core == null) return null;
        var project = await _context.Projects.AsNoTracking().Include(x => x.Coop)
            .FirstAsync(x => x.ProjectId == core.pr.ProjectId);
        var tor = await (from link in _context.PhaseAssignTorItems.AsNoTracking()
                         join item in _context.ProjectTorItems.AsNoTracking() on link.TorItemId equals item.TorItemId
                         where link.AssignId == id orderby item.SortOrder, item.TorItemId
                         select new DevelopmentTorRow { Item = item, Status = link.CheckStatus, Remark = link.Remark, CheckedAt = link.CheckedAt }).ToListAsync();
        var scenarios = await (from link in _context.PhaseAssignTestScenarios.AsNoTracking()
                               join s in _context.TestScenarios.AsNoTracking() on link.ScenarioId equals s.scenario_id
                               where link.AssignId == id && link.IsRequired orderby s.sort_order, s.scenario_id select s).ToListAsync();
        var runs = await _context.TestScenarioRuns.AsNoTracking().Where(x => x.AssignId == id)
            .OrderByDescending(x => x.RoundNo).ThenByDescending(x => x.RunId).ToListAsync();
        var rows = scenarios.Select(s =>
        {
            var devRun = runs.FirstOrDefault(x => x.ScenarioId == s.scenario_id && x.TestStage == "DEV");
            var baRun = runs.FirstOrDefault(x => x.ScenarioId == s.scenario_id && x.TestStage == "BA");
            if (devRun?.TestedAt != null && (baRun?.TestedAt == null || baRun.TestedAt < devRun.TestedAt)) baRun = null;
            return new DevelopmentScenarioRow { Scenario = s, DevRun = devRun, BaRun = baRun };
        }).ToList();
        var torPct = tor.Count == 0 ? 0 : tor.Count(x => x.Status == "COMPLETED") * 100 / tor.Count;
        var devPct = rows.Count == 0 ? 0 : rows.Count(x => x.DevRun?.ResultStatus == "PASS") * 100 / rows.Count;
        var baPct = rows.Count == 0 ? 0 : rows.Count(x => x.BaRun?.ResultStatus == "PASS") * 100 / rows.Count;
        return new DevelopmentWorkflowViewModel { Assignment = core.a, Phase = core.p, Project = project, Employee = core.e,
            TorItems = tor, Scenarios = rows, TorPercent = torPct, DevPercent = devPct, BaPercent = baPct,
            TotalPercent = (torPct * 50 + devPct * 25 + baPct * 25) / 100 };
    }
}
