using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models;

[Table("meeting_calendars")]
public class MeetingCalendar
{
    [Key, Column("calendar_id")]
    public int CalendarId { get; set; }

    [Column("group_id")]
    public int GroupId { get; set; }

    [Required, MaxLength(150), Column("calendar_name")]
    public string CalendarName { get; set; } = "";

    [Required, MaxLength(20), Column("cover_color")]
    public string CoverColor { get; set; } = "#14b8a6";

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [ForeignKey(nameof(GroupId))]
    public MeetingGroup? Group { get; set; }

    public ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();
}
