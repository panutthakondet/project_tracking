using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("phase_assign_logs")]
    public class PhaseAssignLog
    {
        [Key]
        [Column("log_id")]
        public int LogId { get; set; }

        [Required]
        [Column("assign_id")]
        public int AssignId { get; set; }

        [Required]
        [Column("status")]
        public string Status { get; set; } = null!; // PASS / REWORK

        [MaxLength(1000)]
        [Column("remark")]
        public string? Remark { get; set; }

        [Column("round_no")]
        public int? RoundNo { get; set; }

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        // 🔗 relation
        [ForeignKey(nameof(AssignId))]
        public PhaseAssign? PhaseAssign { get; set; }
    }
}