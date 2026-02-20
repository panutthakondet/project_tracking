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

        // ❗ ห้ามยุ่งกับ phase_order: ใช้สำหรับเรียงตามลำดับ phase จาก project_phase
        [Column("phase_order")]
        public int? PhaseOrder { get; set; }

        // ✅ เตรียมไว้ทำสลับแถว (drag & drop) แบบถาวร
        [Column("phase_sort")]
        public int? PhaseSort { get; set; }

        [Required]
        [Column("emp_id")]
        public int EmpId { get; set; }

        // role ใน MySQL เป็น varchar(500) และอนุญาตให้เป็น NULL
        [MaxLength(500)]
        [Column("role")]
        public string? Role { get; set; }

        [Column("plan_start")]
        public DateTime? PlanStart { get; set; }

        [Column("plan_end")]
        public DateTime? PlanEnd { get; set; }

        [Column("actual_start")]
        public DateTime? ActualStart { get; set; }

        [Column("actual_end")]
        public DateTime? ActualEnd { get; set; }

        // remark ใน MySQL เป็น varchar(255)
        [MaxLength(255)]
        [Column("remark")]
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