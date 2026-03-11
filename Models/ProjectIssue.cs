using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    public class ProjectIssue
    {
        // ===== PRIMARY KEY =====
        [Key]
        public int IssueId { get; set; }

        // ===== Project =====
        [Required]
        public int ProjectId { get; set; }
        public virtual Project? Project { get; set; }

        // ===== Issue Info =====
        [Required]
        [StringLength(500)]
        public string IssueName { get; set; } = "";

        // ===== BA Detail =====
        [Column(TypeName = "text")]
        public string? IssueDetail { get; set; }

        // ===== Developer Fix Detail =====
        [Column(TypeName = "text")]
        public string? DevDetail { get; set; }

        // ===== Employee FK =====
        [Required]
        public int EmpId { get; set; }

        [ForeignKey(nameof(EmpId))]
        public virtual Employee? Employee { get; set; }

        // ⭐ ใช้แสดงผลในหน้า View เท่านั้น (ไม่เก็บ DB)
        [NotMapped]
        public string EmpName => Employee?.EmpName ?? "";

        // ===== Status =====
        [Required]
        [StringLength(20)]
        [Column("IssueStatus", TypeName = "varchar(20)")]
        public string IssueStatus { get; set; } = "OPEN";

        // ===== Dev Status (Programmer) =====
        // TODO / DOING / FIXED / BLOCK
        [Required]
        [StringLength(20)]
        [Column("DevStatus", TypeName = "varchar(20)")]
        public string DevStatus { get; set; } = "TODO";

        // ===== Priority =====
        [Required]
        [StringLength(10)]
        [Column("IssuePriority", TypeName = "varchar(10)")]
        public string IssuePriority { get; set; } = "NORMAL";

        // MySQL datetime mapping
        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // =====================================================
        // 🔁 REOPEN TRACKING  (สำคัญมากสำหรับ MySQL)
        // =====================================================

        // tinyint(1) เท่านั้น MySQL ถึง update
        [Column(TypeName = "tinyint(1)")]
        public bool IsReopen { get; set; } = false;

        [Column(TypeName = "int")]
        public int ReopenCount { get; set; } = 0;

        [Column(TypeName = "datetime")]
        public DateTime? LastFixedAt { get; set; }

        // =====================================================
        // 📷 IMAGES (ก่อนแก้)
        // =====================================================
        [InverseProperty(nameof(ProjectIssueImage.Issue))]
        public virtual ICollection<ProjectIssueImage> Images { get; set; }
            = new List<ProjectIssueImage>();

        // =====================================================
        // 🛠 IMAGES (หลังแก้)
        // =====================================================
        [InverseProperty(nameof(ProjectIssueFixImage.Issue))]
        public virtual ICollection<ProjectIssueFixImage> FixImages { get; set; }
            = new List<ProjectIssueFixImage>();
    }
}