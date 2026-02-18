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

        [Column("position")]
        public string? Position { get; set; }

        [Column("status")]
        public string Status { get; set; } = "ACTIVE";

        // ===== RELATION (เพิ่ม) =====
        [InverseProperty(nameof(ProjectIssue.Employee))]
        public virtual ICollection<ProjectIssue> ProjectIssues { get; set; }
            = new List<ProjectIssue>();
    }
}