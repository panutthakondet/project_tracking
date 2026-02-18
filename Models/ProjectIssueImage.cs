using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    public class ProjectIssueImage
    {
        // ===== PRIMARY KEY =====
        [Key] // ✅ สำคัญมาก
        public int ImageId { get; set; }

        // ===== FK → ProjectIssue =====
        [Required]
        public int IssueId { get; set; }

        [ForeignKey(nameof(IssueId))]
        public ProjectIssue? Issue { get; set; }

        // ===== File Info =====
        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = "";

        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = "";

        // ===== Audit =====
        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }
}