using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.ViewModels;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    [RequireMenu("IssueDashboard.Index")]
    public class IssueDashboardController : Controller
    {
        private readonly AppDbContext _context;

        public IssueDashboardController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var yesterday = today.AddDays(-1);
            var endOfYesterday = today.AddTicks(-1);

            // ✅ ปีปัจจุบัน (สำหรับ filter งานตามปี)
            var yearStart = new DateTime(today.Year, 1, 1);
            var yearEnd = new DateTime(today.Year, 12, 31);

            string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();

            // ✅ await ทีละตัว
            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .ToListAsync();

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .ToListAsync();

            // ================= PHASES (FILTER: CURRENT YEAR) =================
            // ✅ ให้ Phase summary อิง "ปีปัจจุบัน" (ช่วงแผนชนปีนี้)
            var phasesInYear = phases
                .Where(p =>
                    p.PlanStart != null &&
                    p.PlanEnd != null &&
                    p.PlanEnd.Value.Date >= yearStart &&
                    p.PlanStart.Value.Date <= yearEnd
                )
                .ToList();

            // ================= PHASES (TODAY) =================
            // ✅ ปรับ "Doing" ให้ยึดตามแผน PlanStart..PlanEnd (ไม่ใช้ Actual)
            // ✅ และนับเฉพาะ phase ที่อยู่ในปีปัจจุบัน (phasesInYear)
            var phasePlanned = phasesInYear.Count(x =>
                x.PlanStart != null &&
                x.PlanStart.Value.Date > today
            );

            var phaseDoing = phasesInYear.Count(x =>
                x.PlanStart != null &&
                x.PlanEnd != null &&
                x.PlanStart.Value.Date <= today &&
                x.PlanEnd.Value.Date >= today
            );

            var phaseDone = phasesInYear.Count(x =>
                // ถ้ามี ActualEnd ถือว่าจบจริง
                x.ActualEnd != null
                // ถ้าไม่มี ActualEnd แต่ PlanEnd ผ่านแล้ว อาจถือว่าจบตามแผน (ถ้าคุณอยากนับแบบนี้ให้เปิดบรรทัดล่าง)
                // || (x.ActualEnd == null && x.PlanEnd != null && x.PlanEnd.Value.Date < today)
            );

            var phaseOverdue = phasesInYear.Count(x =>
                x.ActualEnd == null &&
                x.PlanEnd != null &&
                x.PlanEnd.Value.Date < today
            );

            // ================= PHASES (YESTERDAY) =================
            var phasePlannedY = phasesInYear.Count(x =>
                x.PlanStart != null &&
                x.PlanStart.Value.Date > yesterday
            );

            var phaseDoingY = phasesInYear.Count(x =>
                x.PlanStart != null &&
                x.PlanEnd != null &&
                x.PlanStart.Value.Date <= yesterday &&
                x.PlanEnd.Value.Date >= yesterday
            );

            var phaseDoneY = phasesInYear.Count(x =>
                x.ActualEnd != null &&
                x.ActualEnd.Value <= endOfYesterday
            );

            var phaseOverdueY = phasesInYear.Count(x =>
                x.ActualEnd == null &&
                x.PlanEnd != null &&
                x.PlanEnd.Value.Date < yesterday
            );

            // ================= ISSUES (TODAY) =================
            int total = issues.Count;
            int open = issues.Count(x => Norm(x.IssueStatus) == "OPEN");
            int wip = issues.Count(x => Norm(x.IssueStatus) == "WIP");
            int fixedCount = issues.Count(x => Norm(x.IssueStatus) == "FIXED");
            int reopenTotal = issues.Sum(x => x.ReopenCount);

            double reopenRate = total == 0
                ? 0
                : Math.Round((issues.Count(x => x.ReopenCount > 0) * 100.0) / total, 1);

            // ================= ISSUES (YESTERDAY) =================
            var existedYesterday = issues.Where(x => x.CreatedAt <= endOfYesterday).ToList();
            int totalY = existedYesterday.Count;

            var existedIds = existedYesterday.Select(x => x.IssueId).ToList();

            var histories = await _context.ProjectIssueStatusHistories
                .AsNoTracking()
                .Where(h => existedIds.Contains(h.IssueId) && h.ChangedAt <= endOfYesterday)
                .OrderBy(h => h.IssueId)
                .ThenByDescending(h => h.ChangedAt)
                .ToListAsync();

            var lastHistoryByIssue = histories
                .GroupBy(h => h.IssueId)
                .ToDictionary(g => g.Key, g => g.First());

            string GetStatusAsOfYesterday(ProjectTracking.Models.ProjectIssue issue)
            {
                if (lastHistoryByIssue.TryGetValue(issue.IssueId, out var h))
                {
                    var st = Norm(h.NewStatus);
                    if (!string.IsNullOrWhiteSpace(st)) return st;
                }
                return Norm(issue.IssueStatus);
            }

            int openY = existedYesterday.Count(x => GetStatusAsOfYesterday(x) == "OPEN");
            int wipY = existedYesterday.Count(x => GetStatusAsOfYesterday(x) == "WIP");
            int fixedY = existedYesterday.Count(x => GetStatusAsOfYesterday(x) == "FIXED");

            int reopenTotalY = existedYesterday.Sum(issue =>
            {
                if (lastHistoryByIssue.TryGetValue(issue.IssueId, out var h)) return h.ReopenCount;
                return issue.ReopenCount;
            });

            int reopenedIssuesY = existedYesterday.Count(issue =>
            {
                if (lastHistoryByIssue.TryGetValue(issue.IssueId, out var h)) return h.ReopenCount > 0;
                return issue.ReopenCount > 0;
            });

            double reopenRateY = totalY == 0
                ? 0
                : Math.Round((reopenedIssuesY * 100.0) / totalY, 1);

            // ================= EMPLOYEE MAP (กัน error FullName / กันชื่อซ้ำ) =================
            // ⚠️ ห้ามใช้ e.FullName ถ้า model ไม่มี field นี้
            // ใช้ EmpName ถ้ามี ไม่งั้น fallback เป็น "EMP#id"
            var employees = await _context.Employees
                .AsNoTracking()
                .Select(e => new
                {
                    e.EmpId,
                    EmpName = string.IsNullOrWhiteSpace(e.EmpName)
                        ? ("EMP#" + e.EmpId)
                        : e.EmpName
                })
                .ToListAsync();

            var empNameById = employees
                .GroupBy(x => x.EmpId)
                .ToDictionary(g => g.Key, g => g.First().EmpName);

            string GetEmpName(int? empId)
            {
                if (empId == null) return "Unknown";
                if (empNameById.TryGetValue(empId.Value, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
                return "EMP#" + empId.Value;
            }

            // ================= OPEN BY OWNER (จาก ProjectIssues) =================
            // ✅ ปรับ field เจ้าของงานให้ตรงของคุณ:
            // - ถ้าเป็น OwnerEmpId ให้เปลี่ยน i.EmpId -> i.OwnerEmpId
            // - ถ้าเป็น AssignedEmpId ให้เปลี่ยน i.EmpId -> i.AssignedEmpId
            var openIssues = issues.Where(i => Norm(i.IssueStatus) == "OPEN").ToList();

            // IMPORTANT:
            // - GroupBy ด้วย EmpId (ไม่ใช่ชื่อ) เพื่อกันชื่อซ้ำ
            var openByOwner = openIssues
                .GroupBy(i => (int?)i.EmpId)
                .Select(g => new
                {
                    EmpId = g.Key,
                    Name = GetEmpName(g.Key),
                    Cnt = g.Count()
                })
                .OrderByDescending(x => x.Cnt)
                .ToList();

            // ================= WORKLOAD OVERLAP BY OWNER (PhaseAssign + ProjectPhase PlanStart/PlanEnd เท่านั้น) =================
            // ✅ เป้าหมาย: ให้เหมือนกราฟ Gantt ที่คุณดู
            // ✅ เพิ่ม: จำกัดข้อมูลเฉพาะ “ปีปัจจุบัน”
            //
            // วิธีคิด Doing=3 -> Overlap=2:
            // 1) ต่อคน (emp_id) หาช่วงเวลา “ต่อโปรเจค” จาก phase_assign + project_phase:
            //    emp + project => start = MIN(PlanStart), end = MAX(PlanEnd) ของทุก phase ที่ถูก assign
            // 2) ต่อคน ทำ sweep-line หา "จำนวนโปรเจคที่ซ้อนกันสูงสุด" (MaxConcurrent)
            //    Doing = MaxConcurrent
            //    Overlap = max(0, Doing - 1)

            var rawAssign = await (
                from a in _context.PhaseAssigns.AsNoTracking()
                join p in _context.ProjectPhases.AsNoTracking()
                    on a.PhaseId equals p.PhaseId
                where p.PlanStart != null
                      && p.PlanEnd != null

                      // ✅ เอาเฉพาะช่วงที่ “ชนกับปีปัจจุบัน”
                      && p.PlanEnd.Value.Date >= yearStart
                      && p.PlanStart.Value.Date <= yearEnd

                select new
                {
                    a.EmpId,
                    p.ProjectId,
                    PlanStart = p.PlanStart!.Value,
                    PlanEnd = p.PlanEnd!.Value
                }
            ).ToListAsync();

            // 1) รวมช่วงเป็นราย "emp+project" (กัน phase หลายแถวของโปรเจคเดียวกัน)
            // ✅ และ clip ช่วงให้อยู่ในปีปัจจุบันด้วย (ตัดหัว/ตัดท้าย)
            var empProjectIntervals = rawAssign
                .GroupBy(x => new { x.EmpId, x.ProjectId })
                .Select(g =>
                {
                    var minStart = g.Min(v => v.PlanStart.Date);
                    var maxEnd = g.Max(v => v.PlanEnd.Date);

                    var start = (minStart < yearStart) ? yearStart : minStart;
                    var end = (maxEnd > yearEnd) ? yearEnd : maxEnd;

                    return new
                    {
                        EmpId = g.Key.EmpId,
                        ProjectId = g.Key.ProjectId,
                        Start = start,
                        End = end
                    };
                })
                .Where(x => x.Start <= x.End) // กันข้อมูลเพี้ยน/กันช่วงที่โดน clip แล้วไม่เหลือ
                .ToList();

            // helper: หา max overlap (inclusive end)
            static int MaxConcurrentInclusive(List<(DateTime start, DateTime end)> intervals)
            {
                if (intervals == null || intervals.Count == 0) return 0;

                // sweep: start +1, (end+1day) -1 เพราะ end นับรวม
                var events = new List<(DateTime date, int delta)>(intervals.Count * 2);
                foreach (var it in intervals)
                {
                    var s = it.start.Date;
                    var e = it.end.Date;
                    events.Add((s, +1));
                    events.Add((e.AddDays(1), -1));
                }

                events.Sort((a, b) =>
                {
                    var c = a.date.CompareTo(b.date);
                    if (c != 0) return c;
                    return a.delta.CompareTo(b.delta);
                });

                int cur = 0, best = 0;
                foreach (var ev in events)
                {
                    cur += ev.delta;
                    if (cur > best) best = cur;
                }
                return best;
            }

            // 2) ต่อคน: คำนวณ Doing/Overlap ด้วย "Max concurrent projects" (ภายในปีปัจจุบัน)
            var overlapByOwnerCalc = empProjectIntervals
                .GroupBy(x => x.EmpId)
                .Select(g =>
                {
                    var intervals = g
                        .Select(v => (start: v.Start, end: v.End))
                        .ToList();

                    int doing = MaxConcurrentInclusive(intervals);
                    int overlap = Math.Max(0, doing - 1);

                    return new
                    {
                        EmpId = g.Key,
                        EmpName = GetEmpName(g.Key),
                        Doing = doing,
                        Overlap = overlap
                    };
                })
                .Where(x => x.Overlap > 0)
                .OrderByDescending(x => x.Overlap)
                .ThenByDescending(x => x.Doing)
                .ToList();

            List<string> overlapLabels;
            List<int> overlapCounts;
            List<int> overlapDoingCounts;
            List<int> overlapEmpIds;

            if (overlapByOwnerCalc.Count == 0)
            {
                overlapLabels = new List<string> { "No Overlap" };
                overlapCounts = new List<int> { 1 };
                overlapDoingCounts = new List<int> { 0 };
                overlapEmpIds = new List<int> { 0 };
            }
            else
            {
                overlapLabels = overlapByOwnerCalc.Select(x => x.EmpName).ToList();
                overlapCounts = overlapByOwnerCalc.Select(x => x.Overlap).ToList();
                overlapDoingCounts = overlapByOwnerCalc.Select(x => x.Doing).ToList();
                overlapEmpIds = overlapByOwnerCalc.Select(x => x.EmpId).ToList();
            }

            // ================= VIEWMODEL =================
            var vm = new IssueDashboardVM
            {
                // ===== ISSUE (Today) =====
                Total = total,
                Open = open,
                Wip = wip,
                Fixed = fixedCount,
                ReopenTotal = reopenTotal,
                ReopenRate = reopenRate,

                // ===== ISSUE (Yesterday) =====
                TotalYesterday = totalY,
                OpenYesterday = openY,
                WipYesterday = wipY,
                FixedYesterday = fixedY,
                ReopenTotalYesterday = reopenTotalY,
                ReopenRateYesterday = reopenRateY,

                // ===== STATUS DONUT (Today) =====
                StatusLabels = new List<string> { "OPEN", "WIP", "FIXED", "REJECT", "PASS", "FAIL" },
                StatusCounts = new List<int>
                {
                    issues.Count(x => Norm(x.IssueStatus) == "OPEN"),
                    issues.Count(x => Norm(x.IssueStatus) == "WIP"),
                    issues.Count(x => Norm(x.IssueStatus) == "FIXED"),
                    issues.Count(x => Norm(x.IssueStatus) == "REJECT"),
                    issues.Count(x => Norm(x.IssueStatus) == "PASS"),
                    issues.Count(x => Norm(x.IssueStatus) == "FAIL")
                },

                // ===== PRIORITY DONUT (Today) =====
                PriorityLabels = new List<string> { "URGENT", "NORMAL" },
                PriorityCounts = new List<int>
                {
                    issues.Count(x => Norm(x.IssuePriority) == "URGENT"),
                    issues.Count(x => Norm(x.IssuePriority) == "NORMAL")
                },

                // ✅ Open by Owner (กันชื่อซ้ำ)
                OpenByOwnerLabels = openByOwner.Select(x => x.Name).ToList(),
                OpenByOwnerCounts = openByOwner.Select(x => x.Cnt).ToList(),
                OpenByOwnerEmpIds = openByOwner.Select(x => x.EmpId ?? 0).ToList(),

                // ✅ Overlap by Owner (PhaseAssign + ProjectPhase “ช่วงแผนต่อโปรเจค” แล้วหา max overlap) + จำกัดปีปัจจุบัน
                OverlapByOwnerLabels = overlapLabels,
                OverlapByOwnerCounts = overlapCounts,
                OverlapByOwnerDoingCounts = overlapDoingCounts,
                OverlapByOwnerEmpIds = overlapEmpIds,

                // ===== PHASE (Today) =====
                // ✅ นับเฉพาะปีปัจจุบัน เพื่อให้สอดคล้องกับ Overlap/Gantt
                PhaseTotal = phasesInYear.Count,
                PhasePlanned = phasePlanned,
                PhaseDoing = phaseDoing,
                PhaseDone = phaseDone,
                PhaseOverdue = phaseOverdue,

                // ===== PHASE (Yesterday) =====
                PhaseTotalYesterday = phasesInYear.Count,
                PhasePlannedYesterday = phasePlannedY,
                PhaseDoingYesterday = phaseDoingY,
                PhaseDoneYesterday = phaseDoneY,
                PhaseOverdueYesterday = phaseOverdueY,

                // ===== PHASE CHART =====
                PhaseLabels = new List<string> { "Planned", "Doing", "Done", "Overdue" },
                PhaseCounts = new List<int> { phasePlanned, phaseDoing, phaseDone, phaseOverdue }
            };

            return View(vm);
        }
    }
}