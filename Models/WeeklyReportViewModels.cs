namespace ProjectTracking.Models
{
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
    }

    public class WeeklyReportDetailsViewModel
    {
        public WeeklyReport Report { get; set; } = new();
        public bool CanEditDraft { get; set; }
    }
}
