using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        [Column("phase_order")]
        public int PhaseOrder { get; set; }

        // ✅ ใช้สำหรับจัดเรียงถาวร (Drag & Drop) — ไม่เกี่ยวกับ PhaseOrder ที่อนุญาตให้ซ้ำได้
        [Column("phase_sort")]
        public int PhaseSort { get; set; }

        [Column("plan_start")]
        public DateTime? PlanStart { get; set; }

        [Column("plan_end")]
        public DateTime? PlanEnd { get; set; }

        // ✅ เปลี่ยน Mapping ไปที่ชื่อใหม่ใน DB
        [Column("period_start_date")]
        public DateTime? ActualStart { get; set; }

        [Column("period_end_date")]
        public DateTime? ActualEnd { get; set; }

        // Navigation
        public Project? Project { get; set; }
    }
}