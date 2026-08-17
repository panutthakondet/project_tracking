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

        [Column("coop_id")]
        public int? CoopId { get; set; }

        [ForeignKey(nameof(CoopId))]
        public CntMCoop? Coop { get; set; }

        [Column("department_id")]
        public int? DepartmentId { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public ProjectDepartment? Department { get; set; }

        // ======================
        // BASIC INFO
        // ======================
        [Required]
        [Column("project_name")]
        public string ProjectName { get; set; } = string.Empty;

        [NotMapped]
        public string ProjectDisplayName
        {
            get
            {
                var coopName = Coop?.CoopName?.Trim();
                var projectName = ProjectName?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(coopName)
                    ? projectName
                    : $"{coopName} - {projectName}";
            }
        }

        // รายละเอียดเพิ่มเติมของโครงการ
        [Column("project_detail", TypeName = "text")]
        public string? ProjectDetail { get; set; }

        // ======================
        // 👤 BUSINESS ANALYST
        // ======================
        [Column("ba_emp_id")]
        public int? BaEmpId { get; set; }

        [ForeignKey("BaEmpId")]
        public Employee? BA { get; set; }

        // ======================
        // 👤 PROJECT MANAGER
        // ======================
        [Column("pm_emp_id")]
        public int? PmEmpId { get; set; }

        [ForeignKey("PmEmpId")]
        public Employee? PM { get; set; }

        public ICollection<ProjectTeamMember> TeamMembers { get; set; }
            = new List<ProjectTeamMember>();

        [NotMapped]
        public IReadOnlyList<Employee> ProjectManagers => GetRoleMembers(ProjectTeamRoles.ProjectManager, PM);

        [NotMapped]
        public IReadOnlyList<Employee> BusinessAnalysts => GetRoleMembers(ProjectTeamRoles.BusinessAnalyst, BA);

        [NotMapped]
        public string ProjectManagerNames => string.Join(", ", ProjectManagers.Select(e => e.EmpName));

        [NotMapped]
        public string BusinessAnalystNames => string.Join(", ", BusinessAnalysts.Select(e => e.EmpName));

        [Column("start_date")]
        public DateTime? StartDate { get; set; }

        [Column("end_date")]
        public DateTime? EndDate { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("entry_id")]
        public int? EntryId { get; set; }

        [Column("requirement_card_id")]
        public int? RequirementCardId { get; set; }

        [ForeignKey(nameof(RequirementCardId))]
        public RequirementCard? RequirementCard { get; set; }

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

        private IReadOnlyList<Employee> GetRoleMembers(string role, Employee? legacyMember)
        {
            var members = TeamMembers
                .Where(x => string.Equals(x.MemberRole, role, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Employee?.EmpName)
                .Select(x => x.Employee)
                .Where(x => x != null)
                .Cast<Employee>()
                .GroupBy(x => x.EmpId)
                .Select(x => x.First())
                .ToList();

            if (members.Count == 0 && legacyMember != null)
            {
                members.Add(legacyMember);
            }

            return members;
        }
    }
}
