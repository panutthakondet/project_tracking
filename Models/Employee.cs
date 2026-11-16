using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("employee")]
    public class Employee
    {
        [Key]
        [Column("emp_id")]
        public int EmpId { get; set; }

        [Required]
        [Column("emp_name")]
        public required string EmpName { get; set; }   // ⭐ เดิม

        [Column("alias")]
        [StringLength(80, ErrorMessage = "ฉายาต้องไม่เกิน 80 ตัวอักษร")]
        public string? Alias { get; set; }

        [Required(ErrorMessage = "กรุณาเลือกตำแหน่ง")]
        [Column("position_id")]
        public int? PositionId { get; set; }

        [ForeignKey(nameof(PositionId))]
        public virtual EmployeePosition? PositionDefinition { get; set; }

        [NotMapped]
        public string DisplayPosition => PositionDefinition?.PositionName ?? string.Empty;

        [Required(ErrorMessage = "กรุณาเลือกฝ่าย")]
        [Column("department_id")]
        public int? DepartmentId { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public virtual ProjectDepartment? Department { get; set; }

        [Column("status")]
        public string Status { get; set; } = "ACTIVE";

        [Column("login_user_id")]
        public int? LoginUserId { get; set; }

        [ForeignKey("LoginUserId")]
        public virtual LoginUser? LoginUser { get; set; }

        // ===== RELATION (เพิ่ม) =====
        [InverseProperty(nameof(ProjectIssue.Employee))]
        public virtual ICollection<ProjectIssue> ProjectIssues { get; set; }
            = new List<ProjectIssue>();
    }
}
