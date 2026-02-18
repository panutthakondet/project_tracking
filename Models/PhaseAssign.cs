using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("phase_assign")]
    public class PhaseAssign
    {
        [Key]
        [Column("assign_id")]
        public int AssignId { get; set; }

        [Required]
        [Column("phase_id")]
        public int PhaseId { get; set; }

        [Required]
        [Column("emp_id")]
        public int EmpId { get; set; }

        // 🔥 รองรับ PhaseName ยาว ๆ
        [Required(ErrorMessage = "Role is required")]
        [MaxLength(500)]                // <- เพิ่มความยาว
        [Column("role", TypeName = "nvarchar(500)")]  // SQL Server
        public string Role { get; set; } = string.Empty;

        [Column("plan_start")]
        public DateTime? PlanStart { get; set; }

        [Column("plan_end")]
        public DateTime? PlanEnd { get; set; }

        [Column("actual_start")]
        public DateTime? ActualStart { get; set; }

        [Column("actual_end")]
        public DateTime? ActualEnd { get; set; }

        [Column("remark", TypeName = "nvarchar(1000)")] // กัน remark ยาว
        public string? Remark { get; set; }

        // =========================
        // Navigation
        // =========================

        [ForeignKey(nameof(PhaseId))]
        public ProjectPhase? Phase { get; set; }

        [ForeignKey(nameof(EmpId))]
        public Employee? Employee { get; set; }
    }
}