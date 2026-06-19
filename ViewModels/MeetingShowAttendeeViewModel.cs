namespace ProjectTracking.ViewModels
{
    public class MeetingShowAttendeeViewModel
    {
        public int AttendeeId { get; set; }
        public int EmpId { get; set; }
        public string? EmpName { get; set; }
        public string? Position { get; set; }
        public bool EmailSent { get; set; }
        public bool LineSent { get; set; }
        public bool TelegramSent { get; set; }
    }
}
