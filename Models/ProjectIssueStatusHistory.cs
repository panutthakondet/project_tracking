using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    public class ProjectIssueStatusHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int IssueId { get; set; }

        [StringLength(20)]
        [Column(TypeName = "varchar(20)")]
        public string? OldStatus { get; set; }

        [Required]
        [StringLength(20)]
        [Column(TypeName = "varchar(20)")]
        public string NewStatus { get; set; } = "OPEN";

        [Column(TypeName = "tinyint(1)")]
        public bool IsReopen { get; set; } = false;

        [Column(TypeName = "int")]
        public int ReopenCount { get; set; } = 0;

        [Column(TypeName = "datetime")]
        public DateTime ChangedAt { get; set; } = DateTime.Now;

        public int? ChangedByEmpId { get; set; }

        [ForeignKey(nameof(IssueId))]
        public ProjectIssue? Issue { get; set; }
    }
}