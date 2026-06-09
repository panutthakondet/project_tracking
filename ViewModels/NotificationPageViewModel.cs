using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectTracking.ViewModels
{
    public class NotificationPageViewModel
    {
        public bool IsAdmin { get; set; }
        public List<NotificationGroupViewModel> Groups { get; set; } = new();

        public int TotalCount => Groups.Sum(group => group.TotalCount);
    }

    public class NotificationGroupViewModel
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Tone { get; set; } = "";
        public List<NotificationItemViewModel> Items { get; set; } = new();
        public List<NotificationCoopGroupViewModel> CoopGroups { get; set; } = new();

        public int TotalCount => CoopGroups.Count > 0
            ? CoopGroups.Sum(group => group.Items.Count)
            : Items.Count;

        public int UnreadCount => CoopGroups.Count > 0
            ? CoopGroups.Sum(group => group.Items.Count(item => !item.IsRead))
            : Items.Count(item => !item.IsRead);
    }

    public class NotificationCoopGroupViewModel
    {
        public string CoopName { get; set; } = "-";
        public List<NotificationItemViewModel> Items { get; set; } = new();
    }

    public class NotificationItemViewModel
    {
        public int NotificationId { get; set; }
        public string SourceType { get; set; } = "";
        public string Severity { get; set; } = "warning";
        public string SeverityText { get; set; } = "เสี่ยงล่าช้า";
        public string Title { get; set; } = "";
        public string ProjectName { get; set; } = "-";
        public string CoopName { get; set; } = "-";
        public string BaName { get; set; } = "-";
        public string OwnerName { get; set; } = "-";
        public string DateText { get; set; } = "-";
        public string StatusText { get; set; } = "-";
        public string ExtraStatusText { get; set; } = "";
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
