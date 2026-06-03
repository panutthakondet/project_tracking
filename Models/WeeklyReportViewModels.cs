namespace ProjectTracking.Models
{
    public class UserOptionViewModel
    {
        public int UserId { get; set; }
        public int? EmpId { get; set; }
        public string DisplayName { get; set; } = "";
        public string Username { get; set; } = "";
        public string? Position { get; set; }
    }

    public class PendingWorkItemViewModel
    {
        public string Type { get; set; } = "";
        public int SourceId { get; set; }
        public string ProjectName { get; set; } = "";
        public string Title { get; set; } = "";
        public string? OwnerName { get; set; }
        public string? Status { get; set; }
        public DateTime? DueDate { get; set; }
        public string TargetUrl { get; set; } = "";
    }

    public class WeeklyReportFormViewModel
    {
        public WeeklyReport Report { get; set; } = new();
        public List<PendingWorkItemViewModel> PendingItems { get; set; } = new();
        public List<UserOptionViewModel> Users { get; set; } = new();
        public int[] SelectedUserIds { get; set; } = Array.Empty<int>();
    }

    public class WeeklyReportDetailsViewModel
    {
        public WeeklyReport Report { get; set; } = new();
        public List<UserOptionViewModel> Users { get; set; } = new();
        public bool CanEditDraft { get; set; }
    }

    public class MailboxIndexItemViewModel
    {
        public int MessageId { get; set; }
        public string Subject { get; set; } = "";
        public string? Body { get; set; }
        public string SenderName { get; set; } = "";
        public string MessageType { get; set; } = "";
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? ReportId { get; set; }
        public string? ReportStatus { get; set; }
    }

    public class MailboxDetailsViewModel
    {
        public MailboxMessage Message { get; set; } = new();
        public WeeklyReport? Report { get; set; }
        public List<WeeklyReportAttachment> Attachments { get; set; } = new();
        public List<UserOptionViewModel> Users { get; set; } = new();
        public bool CanForward { get; set; }
    }
}
