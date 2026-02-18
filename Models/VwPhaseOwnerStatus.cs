using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Keyless]
    [Table("vw_phase_owner_status")]
    public class VwPhaseOwnerStatus
    {
        // ================= Project =================
        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("project_name")]
        public string ProjectName { get; set; } = "";

        // ================= Phase =================
        [Column("phase_id")]
        public int PhaseId { get; set; }     // ✅ เพิ่มให้ใช้ ThenBy(x => x.PhaseId)

        [Column("phase_order")]
        public int PhaseOrder { get; set; }

        // ================= Employee =================
        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("emp_name")]
        public string EmpName { get; set; } = "";

        [Column("role")]
        public string Role { get; set; } = "";

        // ================= Plan / Actual =================
        [Column("plan_start")]
        public DateTime? PlanStart { get; set; }

        [Column("plan_end")]
        public DateTime? PlanEnd { get; set; }

        [Column("actual_start")]
        public DateTime? ActualStart { get; set; }

        [Column("actual_end")]
        public DateTime? ActualEnd { get; set; }

        [Column("plan_days")]
        public int PlanDays { get; set; }

        [Column("actual_days")]
        public int ActualDays { get; set; }

        // ================= Status =================
        [Column("phase_status")]
        public string PhaseStatus { get; set; } = "";

        [Column("overdue_days")]
        public int OverdueDays { get; set; }

        // ================= Project Period =================
        [Column("start_date")]
        public DateTime? StartDate { get; set; }

        [Column("end_date")]
        public DateTime? EndDate { get; set; }

        // ================= Remark =================
        [Column("remark")]
        public string? Remark { get; set; }
    }
}