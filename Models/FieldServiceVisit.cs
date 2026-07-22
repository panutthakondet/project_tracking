using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models;

[Table("field_service_visits")]
public class FieldServiceVisit
{
    [Key, Column("visit_id")]
    public int VisitId { get; set; }

    [Column("coop_id")]
    public int CoopId { get; set; }

    [Required, StringLength(200), Column("title")]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(50), Column("service_type")]
    public string ServiceType { get; set; } = "MA";

    [Column("visit_date", TypeName = "date")]
    public DateTime VisitDate { get; set; }

    [Column("end_visit_date", TypeName = "date")]
    public DateTime? EndVisitDate { get; set; }

    [Column("start_time", TypeName = "time")]
    public TimeSpan? StartTime { get; set; }

    [Column("end_time", TypeName = "time")]
    public TimeSpan? EndTime { get; set; }

    [StringLength(255), Column("location")]
    public string? Location { get; set; }

    [StringLength(150), Column("contact_name")]
    public string? ContactName { get; set; }

    [StringLength(50), Column("contact_phone")]
    public string? ContactPhone { get; set; }

    [Column("description", TypeName = "text")]
    public string? Description { get; set; }

    [Column("service_result", TypeName = "text")]
    public string? ServiceResult { get; set; }

    [Required, StringLength(30), Column("status")]
    public string Status { get; set; } = "PLANNED";

    [Column("next_visit_date", TypeName = "date")]
    public DateTime? NextVisitDate { get; set; }

    [Column("created_by")]
    public int? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public CntMCoop? Coop { get; set; }
    public ICollection<FieldServiceAssignee> Assignees { get; set; } = new List<FieldServiceAssignee>();
    public ICollection<FieldServiceAttachment> Attachments { get; set; } = new List<FieldServiceAttachment>();
}

[Table("field_service_assignees")]
public class FieldServiceAssignee
{
    [Key, Column("assignee_id")]
    public int AssigneeId { get; set; }

    [Column("visit_id")]
    public int VisitId { get; set; }

    [Column("emp_id")]
    public int EmpId { get; set; }

    public FieldServiceVisit? Visit { get; set; }
    public Employee? Employee { get; set; }
}
