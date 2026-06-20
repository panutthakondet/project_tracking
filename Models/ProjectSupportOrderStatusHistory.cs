using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    public class ProjectSupportOrderStatusHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Column("order_id")]
        public int OrderId { get; set; }

        [StringLength(20)]
        [Column("old_status", TypeName = "varchar(20)")]
        public string? OldStatus { get; set; }

        [Required]
        [StringLength(20)]
        [Column("new_status", TypeName = "varchar(20)")]
        public string NewStatus { get; set; } = "OPEN";

        [Column("is_reopen", TypeName = "tinyint(1)")]
        public bool IsReopen { get; set; } = false;

        [Column("reopen_count", TypeName = "int")]
        public int ReopenCount { get; set; } = 0;

        [Column("changed_at", TypeName = "datetime")]
        public DateTime ChangedAt { get; set; } = DateTime.Now;

        [Column("changed_by_emp_id")]
        public int? ChangedByEmpId { get; set; }

        [ForeignKey(nameof(OrderId))]
        public ProjectSupportOrder? Order { get; set; }
    }
}
