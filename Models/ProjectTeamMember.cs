using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_team_member")]
    public class ProjectTeamMember
    {
        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("member_role")]
        public string MemberRole { get; set; } = ProjectTeamRoles.ProjectManager;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Project? Project { get; set; }
        public Employee? Employee { get; set; }
    }

    public static class ProjectTeamRoles
    {
        public const string ProjectManager = "PM";
        public const string BusinessAnalyst = "BA";
    }
}
