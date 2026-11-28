namespace ProjectTracking.ViewModels
{
    public class LineOverdueSelectionViewModel
    {
        public List<LineOverdueSelectionItemViewModel> Items { get; set; } = new();
        public int TotalCount => Items.Count;
        public int ReadyCount => Items.Count(x => x.Recipients.Any() && x.Recipients.All(r => r.HasNotificationRecipient));
        public int MissingRecipientCount => Items.Count(x => x.Recipients.Any(r => !r.HasNotificationRecipient));
        public int OverdueCount => Items.Count(x => string.Equals(x.Severity, "DANGER", StringComparison.OrdinalIgnoreCase));
        public int RiskCount => Items.Count(x => !string.Equals(x.Severity, "DANGER", StringComparison.OrdinalIgnoreCase));
        public int SentItemCount => Items.Count(x => x.NotificationSendCount > 0);
        public int TotalNotificationSendCount => Items.Sum(x => x.NotificationSendCount);
    }

    public class LineOverdueSelectionItemViewModel
    {
        private string _recipientName = "-";
        private string _recipientRole = "-";
        private bool _hasNotificationRecipient;

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
        public bool HasNotificationRecipient
        {
            get => Recipients.Count == 0 ? _hasNotificationRecipient : Recipients.Any(x => x.HasNotificationRecipient);
            set => _hasNotificationRecipient = value;
        }
        public bool HasMissingNotificationRecipient => Recipients.Any(x => !x.HasNotificationRecipient);
        public string RecipientLinkText
        {
            get
            {
                if (Recipients.Count == 0)
                    return _hasNotificationRecipient ? $"LINK {_recipientName}" : "ยังไม่มีช่องทางแจ้งเตือน";

                var linked = Recipients
                    .Where(x => x.HasNotificationRecipient)
                    .Select(x => string.IsNullOrWhiteSpace(x.Username) ? x.Name : x.Username)
                    .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
                    .Distinct()
                    .ToList();

                return linked.Count > 0
                    ? $"LINK {string.Join(", ", linked)}"
                    : "ยังไม่มีช่องทางแจ้งเตือน";
            }
        }
        public int NotificationSendCount { get; set; }
        public DateTime? LastNotificationSentAt { get; set; }
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
        public bool HasNotificationRecipient { get; set; }
        public string TargetUrl { get; set; } = "/";
    }
}
