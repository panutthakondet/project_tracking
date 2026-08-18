using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_departments")]
    public class ProjectDepartment
    {
        [Key]
        [Column("department_id")]
        public int DepartmentId { get; set; }

        [Required, StringLength(50)]
        [Column("department_code")]
        public string DepartmentCode { get; set; } = string.Empty;

        [Required, StringLength(150)]
        [Column("department_name")]
        public string DepartmentName { get; set; } = string.Empty;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
        public ICollection<TestTemplateGroupControl> TestTemplateGroupControls { get; set; } = new List<TestTemplateGroupControl>();
    }
}
