using System;
using System.Collections.Generic;

namespace ProjectTracking.ViewModels
{
    public class IssueDashboardVM
    {
        // ================= ISSUE SUMMARY =================
        public int Total { get; set; }
        public int Open { get; set; }
        public int Wip { get; set; }
        public int Fixed { get; set; }
        public int ReopenTotal { get; set; }
        public double ReopenRate { get; set; }

        // ===== Trend: Yesterday =====
        public int TotalYesterday { get; set; }
        public int OpenYesterday { get; set; }
        public int WipYesterday { get; set; }
        public int FixedYesterday { get; set; }
        public int ReopenTotalYesterday { get; set; }
        public double ReopenRateYesterday { get; set; }

        // ===== Helpers: Diff (วันนี้ - เมื่อวาน) =====
        public int TotalDiff => Total - TotalYesterday;
        public int OpenDiff => Open - OpenYesterday;
        public int WipDiff => Wip - WipYesterday;
        public int FixedDiff => Fixed - FixedYesterday;
        public int ReopenTotalDiff => ReopenTotal - ReopenTotalYesterday;
        public double ReopenRateDiff => ReopenRate - ReopenRateYesterday;

        // ================= ISSUE CHARTS =================
        public List<string> StatusLabels { get; set; } = new();
        public List<int> StatusCounts { get; set; } = new();

        public List<string> PriorityLabels { get; set; } = new();
        public List<int> PriorityCounts { get; set; } = new();

        // ================= OPEN BY OWNER =================
        public List<string> OpenByOwnerLabels { get; set; } = new();
        public List<int> OpenByOwnerCounts { get; set; } = new();

        // ✅ EmpIds สำหรับ Open by Owner (กันชื่อซ้ำ + ใช้ query/filter ได้ตรงคน)
        public List<int> OpenByOwnerEmpIds { get; set; } = new();

        // ================= WORKLOAD OVERLAP BY OWNER =================
        // ✅ (อัปเดตแนวคิดล่าสุด) Doing = “จำนวนโปรเจคที่ซ้อนกันสูงสุด” ต่อคน (MaxConcurrent)
        // Overlap = max(0, Doing - 1)
        // * คำนวณจาก PhaseAssign + ProjectPhase (PlanStart..PlanEnd) เท่านั้น และ filter ปีปัจจุบัน
        public List<string> OverlapByOwnerLabels { get; set; } = new();
        public List<int> OverlapByOwnerCounts { get; set; } = new();

        // ✅ EmpIds สำหรับ Overlap by Owner (กันชื่อซ้ำ + ใช้ query/filter ได้ตรงคน)
        public List<int> OverlapByOwnerEmpIds { get; set; } = new();

        // ✅ Doing ต่อคน (ไว้โชว์ tooltip Doing=3 → Overlap=2)
        public List<int> OverlapByOwnerDoingCounts { get; set; } = new();

        // ================= PHASE TRACKING =================
        public int PhaseTotal { get; set; }
        public int PhasePlanned { get; set; }
        public int PhaseDoing { get; set; }
        public int PhaseDone { get; set; }
        public int PhaseOverdue { get; set; }

        // ===== Phase Trend: Yesterday =====
        public int PhaseTotalYesterday { get; set; }
        public int PhasePlannedYesterday { get; set; }
        public int PhaseDoingYesterday { get; set; }
        public int PhaseDoneYesterday { get; set; }
        public int PhaseOverdueYesterday { get; set; }

        // ===== Phase Diff =====
        public int PhaseTotalDiff => PhaseTotal - PhaseTotalYesterday;
        public int PhasePlannedDiff => PhasePlanned - PhasePlannedYesterday;
        public int PhaseDoingDiff => PhaseDoing - PhaseDoingYesterday;
        public int PhaseDoneDiff => PhaseDone - PhaseDoneYesterday;
        public int PhaseOverdueDiff => PhaseOverdue - PhaseOverdueYesterday;

        // ===== Phase Chart =====
        public List<string> PhaseLabels { get; set; } = new();
        public List<int> PhaseCounts { get; set; } = new();

        // ================= OPTIONAL HELPERS (ไม่ลบของเดิม เพิ่มเฉยๆ) =================
        // ✅ กันกราฟ/JS พังเวลา list ไม่เท่ากัน
        public bool HasOpenByOwner =>
            OpenByOwnerLabels != null && OpenByOwnerCounts != null &&
            OpenByOwnerLabels.Count == OpenByOwnerCounts.Count &&
            OpenByOwnerEmpIds != null && OpenByOwnerEmpIds.Count == OpenByOwnerLabels.Count;

        public bool HasOverlapByOwner =>
            OverlapByOwnerLabels != null && OverlapByOwnerCounts != null &&
            OverlapByOwnerLabels.Count == OverlapByOwnerCounts.Count &&
            OverlapByOwnerEmpIds != null && OverlapByOwnerEmpIds.Count == OverlapByOwnerLabels.Count &&
            OverlapByOwnerDoingCounts != null && OverlapByOwnerDoingCounts.Count == OverlapByOwnerLabels.Count;

        // ✅ (เพิ่ม) เผื่ออยากโชว์ปีที่กำลัง filter อยู่ (Controller สามารถ set ได้)
        public int CurrentYear { get; set; } = DateTime.Today.Year;

        // ✅ (เพิ่ม) เผื่ออยากโชว์ช่วงวันที่ที่ filter (Controller set ได้)
        public DateTime? YearStart { get; set; }
        public DateTime? YearEnd { get; set; }
    }
}