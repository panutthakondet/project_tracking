using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;

namespace ProjectTracking.Models
{
    [Table("project_phase")]
    public class ProjectPhase
    {
        [Key]
        [Column("phase_id")]
        public int PhaseId { get; set; }

        [Required]
        [Column("project_id")]
        public int ProjectId { get; set; }

        [Required]
        [Column("phase_name")]
        public string PhaseName { get; set; } = string.Empty;

        // ✅ เพิ่ม PhaseType (งานหลัก / งานรอง)
        [Required]
        [Column("phase_type")]
        [StringLength(20)]
        public string PhaseType { get; set; } = "MAIN";

        // ส่วนงาน เช่น ส่วนที่ 1, ส่วนที่ 2
        [Column("phase_order")]
        public int PhaseOrder { get; set; }

        // งวดงานภายในแต่ละส่วน เช่น งวดที่ 1, งวดที่ 2
        [Column("period_order")]
        public int PeriodOrder { get; set; } = 1;

        // ✅ ใช้สำหรับจัดเรียงถาวร (Drag & Drop) — ไม่เกี่ยวกับ PhaseOrder ที่อนุญาตให้ซ้ำได้
        [Column("phase_sort")]
        public int PhaseSort { get; set; }

        [Column("plan_start")]
        public DateTime? PlanStart { get; set; }

        [Column("plan_end")]
        public DateTime? PlanEnd { get; set; }

        [Column("submitted_date")]
        public DateTime? SubmittedDate { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("entry_id")]
        public int? EntryId { get; set; }

        [NotMapped]
        public string PhasePeriodLabel => $"ส่วนที่ {PhaseOrder} งวดที่ {PeriodOrder}";

        [NotMapped]
        public string PhaseDisplayName => $"{PhasePeriodLabel} - {PhaseName}";

        // วันที่กำหนดส่งงวดงาน
        [Column("period_end_date")]
        public DateTime? PeriodEndDate { get; set; }

        // =============================
        // Backward compatibility
        // อย่ากระทบของเดิม
        // =============================
        [NotMapped]
        public DateTime? ActualEnd
        {
            get => PeriodEndDate;
            set => PeriodEndDate = value;
        }

        // ✅ สถานะงวดงาน
        [Column("phase_status")]
        [StringLength(100)]
        public string? PhaseStatus { get; set; } = "วางแผน";

        [Column("status_id")]
        public int? StatusId { get; set; }

        [ForeignKey(nameof(StatusId))]
        public ProjectPhaseStatusDefinition? StatusDefinition { get; set; }

        [NotMapped]
        public string StatusDescription => StatusDefinition?.StatusDesc ?? PhaseStatus ?? "-";

        // Navigation
        public Project? Project { get; set; }
    }
}
