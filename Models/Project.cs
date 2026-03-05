using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project")] // ✅ ชื่อตารางตรงกับ DB
    public class Project
    {
        // ======================
        // PRIMARY KEY
        // ======================
        [Key]
        [Column("project_id")]
        public int ProjectId { get; set; }

        // ======================
        // BASIC INFO
        // ======================
        [Required]
        [Column("project_name")]
        public string ProjectName { get; set; } = string.Empty;

        // ======================
        // 👤 BUSINESS ANALYST
        // ======================
        [Column("ba_emp_id")]
        public int? BaEmpId { get; set; }

        [ForeignKey("BaEmpId")]
        public Employee? BA { get; set; }

        [Column("start_date")]
        public DateTime? StartDate { get; set; }

        [Column("end_date")]
        public DateTime? EndDate { get; set; }

        [Column("status")]
        public string Status { get; set; } = "PLAN";

        // ======================
        // 🔗 FIGMA LINK
        // ======================
        [Column("figma_link")]
        [StringLength(500)]
        public string? FigmaLink { get; set; }

        // ======================
        // 🆕 SYSTEM / DATABASE INFO
        // ======================

        // 🔗 ชื่อลิงก์ระบบ
        [Column("link_name")]
        [StringLength(150)]
        public string? LinkName { get; set; }

        // 🗄 ฐานข้อมูลที่ใช้
        [Column("database_name")]
        [StringLength(150)]
        public string? DatabaseName { get; set; }

        // 🧪 ทะเบียนที่ใช้ทดสอบ
        [Column("test_account")]
        [StringLength(150)]
        public string? TestAccount { get; set; }

        // 🌐 ลิงก์ Remote / URL
        [Column("remote_url")]
        [StringLength(255)]
        public string? RemoteUrl { get; set; }
    }
}