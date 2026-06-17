namespace ProjectTracking.ViewModels
{
    public class LineOverdueSelectionViewModel
    {
        public List<LineOverdueSelectionItemViewModel> Items { get; set; } = new();
        public int TotalCount => Items.Count;
        public int ReadyCount => Items.Count(x => x.Recipients.Any() && x.Recipients.All(r => r.HasLineRecipient));
        public int MissingLineCount => Items.Count(x => x.Recipients.Any(r => !r.HasLineRecipient));
        public int OverdueCount => Items.Count(x => string.Equals(x.Severity, "DANGER", StringComparison.OrdinalIgnoreCase));
        public int RiskCount => Items.Count(x => !string.Equals(x.Severity, "DANGER", StringComparison.OrdinalIgnoreCase));
        public int SentItemCount => Items.Count(x => x.LineSendCount > 0);
        public int TotalLineSendCount => Items.Sum(x => x.LineSendCount);
    }

    public class LineOverdueSelectionItemViewModel
    {
        private string _recipientName = "-";
        private string _recipientRole = "-";
        private bool _hasLineRecipient;

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
        public int? RecipientUserId { get; set; }
        public List<LineOverdueRecipientViewModel> Recipients { get; set; } = new();
        public string RecipientName
        {
            get => Recipients.Count == 0 ? _recipientName : string.Join(", ", Recipients.Select(x => x.Name));
            set => _recipientName = string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
        public string RecipientRole
        {
            get => Recipients.Count == 0 ? _recipientRole : string.Join(", ", Recipients.Select(x => x.Role).Distinct());
            set => _recipientRole = string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
        public string OwnerName { get; set; } = "-";
        public string OwnerAvatarPath { get; set; } = "/images/Profile/profile.png";
        public string BaName { get; set; } = "-";
        public string BaAvatarPath { get; set; } = "/images/Profile/profile.png";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? DueDate { get; set; }
        public int OverdueDays { get; set; }
        public bool HasLineRecipient
        {
            get => Recipients.Count == 0 ? _hasLineRecipient : Recipients.Any(x => x.HasLineRecipient);
            set => _hasLineRecipient = value;
        }
        public bool HasMissingLineRecipient => Recipients.Any(x => !x.HasLineRecipient);
        public string LineLinkText
        {
            get
            {
                if (Recipients.Count == 0)
                    return _hasLineRecipient ? $"LINK {_recipientName}" : "LINE ยังไม่ลิงค์";

                var linked = Recipients
                    .Where(x => x.HasLineRecipient)
                    .Select(x => string.IsNullOrWhiteSpace(x.Username) ? x.Name : x.Username)
                    .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
                    .Distinct()
                    .ToList();

                return linked.Count > 0
                    ? $"LINK {string.Join(", ", linked)}"
                    : "LINE ยังไม่ลิงค์";
            }
        }
        public int LineSendCount { get; set; }
        public DateTime? LastLineSentAt { get; set; }
        public string TargetUrl { get; set; } = "/";
        public string Message { get; set; } = "";
    }

    public class LineOverdueRecipientViewModel
    {
        public int EmpId { get; set; }
        public int? UserId { get; set; }
        public string? Username { get; set; }
        public string Name { get; set; } = "-";
        public string Role { get; set; } = "-";
        public bool HasLineRecipient { get; set; }
        public string TargetUrl { get; set; } = "/";
    }
}
