using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace ProjectTracking.Controllers
{
    public class PhaseAssignsController : BaseController
    {
        private readonly AppDbContext _context;

        public PhaseAssignsController(AppDbContext context)
        {
            _context = context;
        }

        // =====================================================
        // INDEX
        // =====================================================
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Index(int? projectId, int? empId)
        {
            ViewBag.Projects = await _context.Projects
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;

            ViewBag.EmployeeList = new List<Employee>();

            if (projectId == null)
                return View(new List<PhaseAssign>());

            var project = await _context.Projects.FindAsync(projectId);
            if (project == null)
                return View(new List<PhaseAssign>());

            ViewBag.SelectedProject = project;

            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) เพราะ EF อาจสร้าง JOIN ด้วยคอลัมน์ PhaseId2 (ไม่มีจริงใน DB)
            // ใช้ JOIN ตรงกับ ProjectPhases/Employees โดยอิง FK จริง PhaseId/EmpId
            ViewBag.EmployeeList = await (
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                where ph.ProjectId == projectId
                group new { e.EmpId, e.EmpName } by new { e.EmpId, e.EmpName } into g
                orderby g.Key.EmpName
                select new Employee
                {
                    EmpId = g.Key.EmpId,
                    EmpName = g.Key.EmpName
                }
            ).ToListAsync();

            var assignsQuery =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                where ph.ProjectId == projectId
                select new PhaseAssign
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    PhaseOrder = a.PhaseOrder,
                    PhaseSort = a.PhaseSort,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    ActualStart = a.ActualStart,
                    ActualEnd = a.ActualEnd,
                    Remark = a.Remark,

                    Phase = ph,
                    Employee = e
                };

            if (empId.HasValue)
                assignsQuery = assignsQuery.Where(x => x.EmpId == empId.Value);

            var assigns = await assignsQuery
                // เรียงตาม phase_order (ห้ามแก้ค่า แค่ใช้จัดเรียง), แล้วตาม phase_sort (สำหรับสลับแถว), แล้วค่อย fallback ด้วย assign_id
                .OrderBy(a => a.PhaseOrder ?? int.MaxValue)
                .ThenBy(a => a.PhaseSort ?? int.MaxValue)
                .ThenBy(a => a.AssignId)
                .ToListAsync();

            return View(assigns);
        }

        // =====================================================
        // CREATE (GET)
        // =====================================================
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Create(int projectId)
        {
            var project = await _context.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            ViewBag.ProjectId = projectId;
            ViewBag.ProjectName = project.ProjectName;

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .OrderBy(p => p.PhaseId)
                .ToListAsync();

            // ✅ ใช้ได้ 2 แบบ:
            // 1) View เดิมที่ใช้ SelectList
            ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseName");
            // 2) View ใหม่ที่ต้องการ data-* หรือทำ map ใน JS
            ViewBag.PhaseItems = phases;

            // ✅ เติมค่าเริ่มต้นให้แสดงทันที (กรณีมี phase อย่างน้อย 1)
            var firstPhase = phases.FirstOrDefault();
            var defaultModel = new PhaseAssign();
            if (firstPhase != null)
            {
                defaultModel.PhaseId = firstPhase.PhaseId;
                defaultModel.Role = firstPhase.PhaseName;
                defaultModel.PlanStart = firstPhase.PlanStart;
                defaultModel.PlanEnd = firstPhase.PlanEnd;
            }

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName");

            return View(defaultModel);
        }

        // =====================================================
        // CREATE (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Create(PhaseAssign model, int projectId)
        {
            var phase = await _context.ProjectPhases
                .FirstOrDefaultAsync(p => p.PhaseId == model.PhaseId);

            if (phase == null)
            {
                ModelState.AddModelError("", "Phase not found");
            }
            else
            {
                // 1) ✅ ใช้ค่าในช่อง 🎯 Role (Auto) เป็นค่าที่ insert ลง phase_assign.role
                // รองรับหลายชื่อ field กันพัง (บาง View ตั้งชื่อไม่เหมือนกัน)
                string roleAuto =
                    (Request.Form["RoleAuto"].FirstOrDefault() ??
                     Request.Form["Role_Auto"].FirstOrDefault() ??
                     Request.Form["Role(Auto)"].FirstOrDefault() ??
                     Request.Form["roleAuto"].FirstOrDefault() ??
                     "").Trim();

                // ถ้า View ผูกช่อง Role (Auto) เข้ากับ model.Role มาแล้ว ก็ใช้ model.Role ได้
                if (string.IsNullOrWhiteSpace(roleAuto))
                    roleAuto = (model.Role ?? "").Trim();

                // ถ้ายังว่างอยู่ ค่อย fallback เป็นชื่อ Phase
                model.Role = !string.IsNullOrWhiteSpace(roleAuto) ? roleAuto : phase.PhaseName;

                // 2) ดึงวันจาก project_phase
                model.PlanStart = phase.PlanStart;
                model.PlanEnd = phase.PlanEnd;

                // 3) สำคัญ: บันทึก phase_order จาก project_phase ลง phase_assign
                model.PhaseOrder = phase.PhaseOrder;
            }

            if (!ModelState.IsValid)
            {
                await ReloadCreateDropdown(projectId, model);
                return View(model);
            }

            // ✅ phase_sort is NOT NULL in MySQL, so always set a value
            // Keep a single total order per project (same order used by drag-reorder on Index)
            if (model.PhaseSort == null || model.PhaseSort <= 0)
            {
                var maxSort = await (
                    from a in _context.PhaseAssigns.AsNoTracking()
                    join ph2 in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph2.PhaseId
                    where ph2.ProjectId == projectId
                    select (int?)a.PhaseSort
                ).MaxAsync();

                model.PhaseSort = (maxSort ?? 0) + 1;
            }
            _context.PhaseAssigns.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = phase!.ProjectId });
        }

        // =====================================================
        // EDIT (GET)
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Edit(int id)
        {
            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) เพื่อเลี่ยง EF สร้าง/อ้าง PhaseId2
            var assign = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (assign == null)
                return NotFound();

            // หา ProjectId จาก PhaseId ผ่าน ProjectPhases
            var projectId = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == assign.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            ViewBag.ProjectId = projectId;

            // ✅ For Edit view: allow changing Phase
            if (projectId.HasValue)
            {
                var phases = await _context.ProjectPhases
                    .AsNoTracking()
                    .Where(p => p.ProjectId == projectId.Value)
                    .OrderBy(p => p.PhaseId)
                    .ToListAsync();

                ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseName", assign.PhaseId);
                ViewBag.PhaseItems = phases;
            }

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                assign.EmpId
            );

            return View(assign);
        }

        // =====================================================
        // EDIT (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Edit(int id, PhaseAssign model)
        {
            if (id != model.AssignId)
                return NotFound();

            var db = await _context.PhaseAssigns
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (db == null)
                return NotFound();

            // ✅ Determine project of this assignment (from current db.PhaseId)
            var projectIdOfAssign = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == db.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            if (projectIdOfAssign == null)
            {
                ModelState.AddModelError("", "Cannot determine project for this assignment.");
            }

            // ✅ Validate selected phase (must be in same project)
            ProjectPhase? selectedPhase = null;
            if (model.PhaseId > 0)
            {
                selectedPhase = await _context.ProjectPhases
                    .FirstOrDefaultAsync(p => p.PhaseId == model.PhaseId);

                if (selectedPhase == null)
                {
                    ModelState.AddModelError(nameof(model.PhaseId), "Phase not found");
                }
                else if (projectIdOfAssign != null && selectedPhase.ProjectId != projectIdOfAssign.Value)
                {
                    ModelState.AddModelError(nameof(model.PhaseId), "Selected phase is not in this project");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(model.PhaseId), "Phase is required");
            }

            if (!ModelState.IsValid)
            {
                // Reload dropdowns for Edit view
                if (projectIdOfAssign.HasValue)
                {
                    var phases = await _context.ProjectPhases
                        .AsNoTracking()
                        .Where(p => p.ProjectId == projectIdOfAssign.Value)
                        .OrderBy(p => p.PhaseId)
                        .ToListAsync();

                    ViewBag.ProjectId = projectIdOfAssign;
                    ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseName", model.PhaseId);
                    ViewBag.PhaseItems = phases;
                }

                ViewBag.Employees = new SelectList(
                    await _context.Employees
                        .Where(e => e.Status == "ACTIVE")
                        .OrderBy(e => e.EmpName)
                        .ToListAsync(),
                    "EmpId",
                    "EmpName",
                    model.EmpId
                );

                return View(model);
            }

            // ✅ Apply Phase change + sync plan dates/order from selected phase
            if (selectedPhase != null)
            {
                db.PhaseId = selectedPhase.PhaseId;
                db.PlanStart = selectedPhase.PlanStart;
                db.PlanEnd = selectedPhase.PlanEnd;
                db.PhaseOrder = selectedPhase.PhaseOrder;
            }

            db.EmpId = model.EmpId;
            db.PlanStart = model.PlanStart;
            db.PlanEnd = model.PlanEnd;
            db.ActualStart = model.ActualStart;
            db.ActualEnd = model.ActualEnd;
            db.Remark = model.Remark;

            // ✅ Role: allow manual edit (fallback to PhaseName)
            var roleText = (model.Role ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(roleText))
            {
                db.Role = roleText;
            }
            else
            {
                db.Role = selectedPhase?.PhaseName ?? db.Role;
            }

            await _context.SaveChangesAsync();

            var projectId = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == db.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            return RedirectToAction(nameof(Index), new { projectId });
        }

        // =====================================================
        // AJAX
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> GetPhasePlan(int phaseId)
        {
            var phase = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == phaseId)
                .Select(p => new
                {
                    role = p.PhaseName,
                    planStart = p.PlanStart,
                    planEnd = p.PlanEnd,
                    phaseOrder = p.PhaseOrder
                })
                .FirstOrDefaultAsync();

            if (phase == null)
                return NotFound();

            // ✅ return เป็น string yyyy-MM-dd เพื่อให้ใส่เข้า <input type="date"> ได้ทันที
            return Json(new
            {
                role = phase.role,
                planStart = phase.planStart.HasValue ? phase.planStart.Value.ToString("yyyy-MM-dd") : "",
                planEnd = phase.planEnd.HasValue ? phase.planEnd.Value.ToString("yyyy-MM-dd") : "",
                phaseOrder = phase.phaseOrder
            });
        }

        // =====================================================
        // PRINT REPORT (⭐ FIX NULL HERE)
        // =====================================================
        [RequireMenu("PhaseAssigns.Print")]
        [HttpGet]
        public async Task<IActionResult> Print(int? projectId, int? empId, string? role)
        {
            ViewBag.Projects = await _context.Projects.OrderBy(p => p.ProjectName).ToListAsync();
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;
            ViewBag.SelectedRole = role;

            ViewBag.EmployeeList = new List<Employee>();
            ViewBag.RoleList = new List<string>();

            if (projectId == null)
                return View(new List<PhaseAssign>());

            ViewBag.SelectedProject = await _context.Projects.FindAsync(projectId);

            // Employee dropdown
            ViewBag.EmployeeList = await (
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                where ph.ProjectId == projectId
                group new { e.EmpId, e.EmpName } by new { e.EmpId, e.EmpName } into g
                orderby g.Key.EmpName
                select new Employee
                {
                    EmpId = g.Key.EmpId,
                    EmpName = g.Key.EmpName
                }
            ).ToListAsync();

            // Role dropdown
            ViewBag.RoleList = await (
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                where ph.ProjectId == projectId && a.Role != null
                group a.Role by a.Role into g
                orderby g.Key
                select g.Key!
            ).ToListAsync();

            var query =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                where ph.ProjectId == projectId
                select new PhaseAssign
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    PhaseOrder = a.PhaseOrder,
                    PhaseSort = a.PhaseSort,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    ActualStart = a.ActualStart,
                    ActualEnd = a.ActualEnd,
                    Remark = a.Remark,

                    Phase = ph,
                    Employee = e
                };

            if (empId.HasValue)
                query = query.Where(x => x.EmpId == empId.Value);

            if (!string.IsNullOrEmpty(role))
                query = query.Where(x => x.Role == role);

            return View(await query
                .OrderBy(a => a.PhaseOrder ?? int.MaxValue)
                .ThenBy(a => a.PhaseSort ?? int.MaxValue)
                .ThenBy(a => a.AssignId)
                .ToListAsync());
        }

        // =====================================================
        // PRINT FORM
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Form(int id)
        {
            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) / ThenInclude(Project) เพื่อเลี่ยง PhaseId2
            // ดึง Assign + Employee ก่อน
            var assign = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (assign == null)
                return NotFound();

            // ดึง Phase (ProjectPhase) แยกต่างหาก
            var phase = await _context.ProjectPhases
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PhaseId == assign.PhaseId);

            if (phase != null)
            {
                // ผูก navigation เพื่อให้ View ที่อ้าง assign.Phase.* ยังทำงานได้
                assign.Phase = phase;

                // ดึง Project และผูกให้ Phase.Project เพื่อให้ View ที่อ้าง Phase.Project.* ยังทำงานได้
                var project = await _context.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(pr => pr.ProjectId == phase.ProjectId);

                if (project != null)
                {
                    phase.Project = project;
                }
            }

            ViewBag.BusinessAnalyst = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Position == "Business Analyst")
                .FirstOrDefaultAsync();

            return View(assign);
        }

        // =====================================================
        // DELETE
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Delete(int id)
        {
            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) เพื่อเลี่ยง PhaseId2
            var assign = await _context.PhaseAssigns
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (assign == null)
                return NotFound();

            var projectId = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == assign.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            _context.PhaseAssigns.Remove(assign);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId });
        }

        // =====================================================
        // REORDER (Drag & Drop -> Persist to phase_sort)
        // =====================================================
        public sealed class ReorderRequest
        {
            [JsonPropertyName("phaseId")]
            public int PhaseId { get; set; }

            // ordered assignIds within the phase
            [JsonPropertyName("assignIds")]
            public List<int> AssignIds { get; set; } = new();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Reorder()
        {
            // ✅ Goal: accept many payload shapes from JS drag/drop and persist to phase_sort
            // Supports:
            // - JSON: { phaseId, assignIds:[...] }
            // - JSON: { phase_id, assign_ids:[...] }
            // - JSON: { phaseId, ids:[...] }
            // - JSON: { phaseId, order:[...] } (numbers or objects with assignId)
            // - JSON: { items:[{assignId:1},{assignId:2}] }
            // - JSON: [1,2,3] (projectId/phaseId via query string)
            // - Form: projectId=.. / phaseId=.. & assignIds=1,2,3 OR assignIds[]=1&assignIds[]=2
            //
            // IMPORTANT CHANGE:
            // - Allow reordering across phases by validating by PROJECT when projectId is provided (UI uses one tbody).
            // - If projectId is not provided, fall back to old behavior (phase-based reorder).

            try
            {
                int phaseId = 0;
                int projectId = 0;
                var ids = new List<int>();
                string? rawBody = null;

                // Query string fallback
                var qProjectId = Request.Query["projectId"].FirstOrDefault()
                              ?? Request.Query["ProjectId"].FirstOrDefault()
                              ?? Request.Query["project_id"].FirstOrDefault();
                int.TryParse(qProjectId, out projectId);

                var qPhaseId = Request.Query["phaseId"].FirstOrDefault()
                            ?? Request.Query["PhaseId"].FirstOrDefault()
                            ?? Request.Query["phase_id"].FirstOrDefault();
                int.TryParse(qPhaseId, out phaseId);

                // 1) Form (application/x-www-form-urlencoded | multipart/form-data)
                if (Request.HasFormContentType)
                {
                    var projectIdStr = Request.Form["projectId"].FirstOrDefault()
                                   ?? Request.Form["ProjectId"].FirstOrDefault()
                                   ?? Request.Form["project_id"].FirstOrDefault();
                    if (projectId <= 0) int.TryParse(projectIdStr, out projectId);

                    var phaseIdStr = Request.Form["phaseId"].FirstOrDefault()
                                   ?? Request.Form["PhaseId"].FirstOrDefault()
                                   ?? Request.Form["phase_id"].FirstOrDefault();
                    if (phaseId <= 0) int.TryParse(phaseIdStr, out phaseId);

                    var csv = Request.Form["assignIds"].FirstOrDefault()
                           ?? Request.Form["AssignIds"].FirstOrDefault()
                           ?? Request.Form["assign_ids"].FirstOrDefault()
                           ?? Request.Form["ids"].FirstOrDefault()
                           ?? Request.Form["Ids"].FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(csv))
                    {
                        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (int.TryParse(part, out var v)) ids.Add(v);
                    }

                    foreach (var k in new[] { "assignIds[]", "AssignIds[]", "assign_ids[]", "ids[]", "Ids[]" })
                    {
                        if (Request.Form.TryGetValue(k, out var values))
                        {
                            foreach (var s in values)
                                if (int.TryParse(s, out var v)) ids.Add(v);
                        }
                    }
                }
                else
                {
                    // 2) JSON / Raw body
                    Request.EnableBuffering();
                    Request.Body.Position = 0;
                    using (var reader = new StreamReader(Request.Body, leaveOpen: true))
                    {
                        rawBody = await reader.ReadToEndAsync();
                    }
                    Request.Body.Position = 0;

                    var body = rawBody ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        // Try strong type first
                        try
                        {
                            var req = JsonSerializer.Deserialize<ReorderRequest>(body, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            if (req != null)
                            {
                                if (phaseId <= 0) phaseId = req.PhaseId;
                                if (req.AssignIds != null) ids.AddRange(req.AssignIds);
                            }
                        }
                        catch
                        {
                            // ignore, will try flexible parsing
                        }

                        // Flexible parsing
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            var root = doc.RootElement;

                            if (root.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var el in root.EnumerateArray())
                                {
                                    if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                    else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var v)) ids.Add(v);
                                }
                            }
                            else if (root.ValueKind == JsonValueKind.Object)
                            {
                                // projectId
                                if (projectId <= 0)
                                {
                                    if (root.TryGetProperty("projectId", out var pid) && pid.ValueKind == JsonValueKind.Number) projectId = pid.GetInt32();
                                    else if (root.TryGetProperty("ProjectId", out var pid2) && pid2.ValueKind == JsonValueKind.Number) projectId = pid2.GetInt32();
                                    else if (root.TryGetProperty("project_id", out var pid3) && pid3.ValueKind == JsonValueKind.Number) projectId = pid3.GetInt32();
                                    else if (root.TryGetProperty("project_id", out var pid4) && pid4.ValueKind == JsonValueKind.String && int.TryParse(pid4.GetString(), out var vpid4)) projectId = vpid4;
                                }

                                // phaseId
                                if (phaseId <= 0)
                                {
                                    if (root.TryGetProperty("phaseId", out var pid) && pid.ValueKind == JsonValueKind.Number) phaseId = pid.GetInt32();
                                    else if (root.TryGetProperty("PhaseId", out var pid2) && pid2.ValueKind == JsonValueKind.Number) phaseId = pid2.GetInt32();
                                    else if (root.TryGetProperty("phase_id", out var pid3) && pid3.ValueKind == JsonValueKind.Number) phaseId = pid3.GetInt32();
                                    else if (root.TryGetProperty("phase_id", out var pid4) && pid4.ValueKind == JsonValueKind.String && int.TryParse(pid4.GetString(), out var vpid4)) phaseId = vpid4;
                                }

                                void ReadIdArray(string propName)
                                {
                                    if (!root.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                                    foreach (var el in arr.EnumerateArray())
                                    {
                                        if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                        else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var v)) ids.Add(v);
                                        else if (el.ValueKind == JsonValueKind.Object)
                                        {
                                            if (el.TryGetProperty("assignId", out var aid) && aid.ValueKind == JsonValueKind.Number) ids.Add(aid.GetInt32());
                                            else if (el.TryGetProperty("AssignId", out var aid2) && aid2.ValueKind == JsonValueKind.Number) ids.Add(aid2.GetInt32());
                                            else if (el.TryGetProperty("assign_id", out var aid3) && aid3.ValueKind == JsonValueKind.Number) ids.Add(aid3.GetInt32());
                                            else if (el.TryGetProperty("id", out var aid4) && aid4.ValueKind == JsonValueKind.Number) ids.Add(aid4.GetInt32());
                                        }
                                    }
                                }

                                ReadIdArray("assignIds");
                                ReadIdArray("AssignIds");
                                ReadIdArray("assign_ids");
                                ReadIdArray("ids");
                                ReadIdArray("Ids");
                                ReadIdArray("order");
                                ReadIdArray("Order");
                                ReadIdArray("sortedIds");
                                ReadIdArray("SortedIds");
                                ReadIdArray("items");
                                ReadIdArray("Items");

                                if (ids.Count == 0 && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                                {
                                    if (data.TryGetProperty("assignIds", out var a1) && a1.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var el in a1.EnumerateArray())
                                            if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                    }
                                    else if (data.TryGetProperty("ids", out var a2) && a2.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var el in a2.EnumerateArray())
                                            if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                    }

                                    if (projectId <= 0)
                                    {
                                        if (data.TryGetProperty("projectId", out var p1) && p1.ValueKind == JsonValueKind.Number) projectId = p1.GetInt32();
                                        else if (data.TryGetProperty("project_id", out var p2) && p2.ValueKind == JsonValueKind.Number) projectId = p2.GetInt32();
                                    }

                                    if (phaseId <= 0)
                                    {
                                        if (data.TryGetProperty("phaseId", out var p1) && p1.ValueKind == JsonValueKind.Number) phaseId = p1.GetInt32();
                                        else if (data.TryGetProperty("phase_id", out var p2) && p2.ValueKind == JsonValueKind.Number) phaseId = p2.GetInt32();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }

                // preserve order (first occurrence wins)
                var seen = new HashSet<int>();
                ids = ids.Where(x => x > 0 && seen.Add(x)).ToList();

                // Infer projectId if missing but we do have assign ids
                if (projectId <= 0 && ids.Count > 0)
                {
                    projectId = await (
                        from a in _context.PhaseAssigns.AsNoTracking()
                        join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                        where a.AssignId == ids[0]
                        select ph.ProjectId
                    ).FirstOrDefaultAsync();
                }

                // If we still don't have projectId, infer phaseId (legacy) if possible
                if (phaseId <= 0 && ids.Count > 0)
                {
                    phaseId = await _context.PhaseAssigns
                        .AsNoTracking()
                        .Where(a => a.AssignId == ids[0])
                        .Select(a => a.PhaseId)
                        .FirstOrDefaultAsync();
                }

                if (ids.Count == 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Invalid payload",
                        projectId,
                        phaseId,
                        count = 0,
                        contentType = Request.ContentType,
                        bodyLength = rawBody?.Length ?? 0
                    });
                }

                // =====================================================
                // ✅ New behavior: project-based reorder (allows cross-phase)
                // =====================================================
                if (projectId > 0)
                {
                    // Load all assigns in the project (so we can keep a stable total order)
                    var allRows = await (
                        from a in _context.PhaseAssigns
                        join ph in _context.ProjectPhases on a.PhaseId equals ph.PhaseId
                        where ph.ProjectId == projectId
                        select a
                    ).ToListAsync();

                    if (allRows.Count == 0)
                        return NotFound(new { ok = false, message = "Project not found or no assignments", projectId });

                    var allMap = allRows.ToDictionary(x => x.AssignId, x => x);

                    // Validate: payload IDs must belong to this project
                    var extra = ids.Where(id => !allMap.ContainsKey(id)).Distinct().ToList();
                    if (extra.Count > 0)
                    {
                        return BadRequest(new
                        {
                            ok = false,
                            message = "Payload contains assignIds that do not belong to this project.",
                            projectId,
                            extraIds = extra
                        });
                    }

                    // For IDs not included in payload, append them after payload in a deterministic order
                    var remaining = allRows
                        .Where(r => !ids.Contains(r.AssignId))
                        .OrderBy(r => r.PhaseSort ?? int.MaxValue)
                        .ThenBy(r => r.AssignId)
                        .Select(r => r.AssignId)
                        .ToList();

                    var finalOrder = ids.Concat(remaining).ToList();

                    int sort = 1;
                    foreach (var id in finalOrder)
                    {
                        if (allMap.TryGetValue(id, out var row))
                        {
                            row.PhaseSort = sort;
                            sort++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    return Ok(new { ok = true, projectId, count = ids.Count, total = allRows.Count });
                }

                // =====================================================
                // ✅ Legacy behavior: phase-based reorder (same as before)
                // =====================================================
                if (phaseId <= 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Missing projectId/phaseId",
                        projectId,
                        phaseId,
                        count = ids.Count
                    });
                }

                var rows = await _context.PhaseAssigns
                    .Where(a => a.PhaseId == phaseId)
                    .ToListAsync();

                if (rows.Count == 0)
                    return NotFound(new { ok = false, message = "Phase not found or no assignments", phaseId });

                var map = rows.ToDictionary(x => x.AssignId, x => x);

                var allIds = map.Keys.OrderBy(x => x).ToList();
                var missing = allIds.Except(ids).ToList();
                var extraPhase = ids.Except(allIds).ToList();

                if (extraPhase.Count > 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Payload contains assignIds that do not belong to this phase.",
                        phaseId,
                        extraIds = extraPhase
                    });
                }

                if (missing.Count > 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Incomplete reorder payload. Please send ALL assignIds in the phase in the new UI order.",
                        phaseId,
                        receivedCount = ids.Count,
                        totalCount = rows.Count,
                        missingIds = missing
                    });
                }

                int sort2 = 1;
                foreach (var id in ids)
                {
                    if (map.TryGetValue(id, out var row))
                    {
                        row.PhaseSort = sort2;
                        sort2++;
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { ok = true, phaseId, count = ids.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, message = ex.Message });
            }
        }
        // =====================================================
        // HELPER
        // =====================================================
        private async Task ReloadCreateDropdown(int projectId, PhaseAssign model)
        {
            ViewBag.ProjectId = projectId;

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .OrderBy(p => p.PhaseId)
                .ToListAsync();

            ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseName", model.PhaseId);
            ViewBag.PhaseItems = phases;

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                model.EmpId);
        }
    }
}