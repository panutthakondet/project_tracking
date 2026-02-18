using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("ProjectIssueFixImages")]
    public class ProjectIssueFixImage
    {
        // ===== PRIMARY KEY =====
        [Key]
        public int ImageId { get; set; }

        // ===== FK → ProjectIssue =====
        [Required]
        public int IssueId { get; set; }

        [ForeignKey(nameof(IssueId))]
        public virtual ProjectIssue? Issue { get; set; }

        // ===== File Info =====
        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        // ===== Audit =====
        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }
}