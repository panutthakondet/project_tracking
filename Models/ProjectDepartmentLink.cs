using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_department_link")]
    public class ProjectDepartmentLink
    {
        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("department_id")]
        public int DepartmentId { get; set; }

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Project? Project { get; set; }
        public ProjectDepartment? Department { get; set; }
    }
}
