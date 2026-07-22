using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models;

[Table("meeting_groups")]
public class MeetingGroup
{
    [Key, Column("group_id")]
    public int GroupId { get; set; }

    [Required, MaxLength(150), Column("group_name")]
    public string GroupName { get; set; } = "";

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<MeetingCalendar> Calendars { get; set; } = new List<MeetingCalendar>();
}
