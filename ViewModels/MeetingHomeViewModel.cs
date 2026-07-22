using ProjectTracking.Models;

namespace ProjectTracking.ViewModels;

public class MeetingHomeViewModel
{
    public List<MeetingGroup> Groups { get; set; } = new();
    public int TotalCalendars { get; set; }
    public int TotalMeetings { get; set; }
    public bool CanCreateGroup { get; set; }
    public bool CanEditGroup { get; set; }
    public bool CanDeleteGroup { get; set; }
    public bool CanCreateCalendar { get; set; }
    public bool CanEditCalendar { get; set; }
    public bool CanDeleteCalendar { get; set; }
}
