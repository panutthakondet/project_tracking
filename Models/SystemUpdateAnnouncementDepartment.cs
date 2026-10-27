using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("system_update_announcement_departments")]
    public class SystemUpdateAnnouncementDepartment
    {
        [Column("update_id")]
        public int UpdateId { get; set; }

        [Column("department_id")]
        public int DepartmentId { get; set; }

        [ForeignKey(nameof(UpdateId))]
        public SystemUpdateAnnouncement? Update { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public ProjectDepartment? Department { get; set; }
    }
}
