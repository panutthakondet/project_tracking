using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectTracking.ViewModels
{
    public class OpenIssueSupportPageViewModel
    {
        public bool IsAdmin { get; set; }
        public string CurrentEmployeeName { get; set; } = "-";
        public List<OpenIssueSupportGroupViewModel> Groups { get; set; } = new();

        public int TotalCount => Groups.Sum(group => group.TotalCount);
    }

    public class OpenIssueSupportGroupViewModel
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Tone { get; set; } = "";
        public List<OpenIssueSupportItemViewModel> Items { get; set; } = new();
        public List<OpenIssueSupportCoopGroupViewModel> CoopGroups { get; set; } = new();

        public int TotalCount => CoopGroups.Count > 0
            ? CoopGroups.Sum(group => group.Items.Count)
            : Items.Count;
    }

    public class OpenIssueSupportCoopGroupViewModel
    {
        public string CoopName { get; set; } = "-";
        public List<OpenIssueSupportItemViewModel> Items { get; set; } = new();
    }

    public class OpenIssueSupportItemViewModel
    {
        public string Type { get; set; } = "";
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string ProjectName { get; set; } = "-";
        public string CoopName { get; set; } = "-";
        public string BaName { get; set; } = "-";
        public string OwnerName { get; set; } = "-";
        public string OwnerAvatarPath { get; set; } = "/images/Profile/profile.png";
        public string Detail { get; set; } = "-";
        public string StatusText { get; set; } = "-";
        public string DevStatusText { get; set; } = "-";
        public string PriorityText { get; set; } = "-";
        public string StartText { get; set; } = "-";
        public string DueText { get; set; } = "-";
        public string DateRangeText { get; set; } = "-";
        public string Severity { get; set; } = "normal";
        public string RecipientRole { get; set; } = "";
        public string TargetUrl { get; set; } = "#";
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
