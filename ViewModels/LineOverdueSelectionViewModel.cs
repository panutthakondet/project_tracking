namespace ProjectTracking.ViewModels
{
    public class LineOverdueSelectionViewModel
    {
        public List<LineOverdueSelectionItemViewModel> Items { get; set; } = new();
        public int TotalCount => Items.Count;
        public int ReadyCount => Items.Count(x => x.HasLineRecipient);
        public int MissingLineCount => Items.Count(x => !x.HasLineRecipient);
    }

    public class LineOverdueSelectionItemViewModel
    {
        public string Key { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public int SourceId { get; set; }
        public string StateText { get; set; } = "";
        public string Severity { get; set; } = "WARNING";
        public string CoopName { get; set; } = "-";
        public string ProjectName { get; set; } = "-";
        public string Title { get; set; } = "-";
        public int RecipientEmpId { get; set; }
        public string RecipientName { get; set; } = "-";
        public string RecipientRole { get; set; } = "-";
        public string OwnerName { get; set; } = "-";
        public string BaName { get; set; } = "-";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? DueDate { get; set; }
        public int OverdueDays { get; set; }
        public bool HasLineRecipient { get; set; }
        public string TargetUrl { get; set; } = "/";
        public string Message { get; set; } = "";
    }
}
