using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectTracking.ViewModels
{
    public class NotificationSendLogPageViewModel
    {
        public string SelectedChannel { get; set; } = "LINE";
        public int? SelectedEmpId { get; set; }
        public int? SelectedDepartmentId { get; set; }
        public List<NotificationSendLogDepartmentViewModel> Departments { get; set; } = new();
        public List<NotificationSendLogTabViewModel> Tabs { get; set; } = new();
        public List<NotificationSendLogUserViewModel> Users { get; set; } = new();
        public List<NotificationSendLogItemViewModel> Logs { get; set; } = new();

        public int TotalCount => Tabs.FirstOrDefault(x => x.Channel == SelectedChannel)?.Count ?? 0;
        public string SelectedChannelLabel => Tabs.FirstOrDefault(x => x.Channel == SelectedChannel)?.Label ?? SelectedChannel;
    }

    public class NotificationSendLogDepartmentViewModel
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "";
    }

    public class NotificationSendLogTabViewModel
    {
        public string Channel { get; set; } = "";
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class NotificationSendLogUserViewModel
    {
        public int EmpId { get; set; }
        public string Name { get; set; } = "-";
        public string Username { get; set; } = "-";
        public string Position { get; set; } = "-";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public int Count { get; set; }
        public DateTime? LastSentAt { get; set; }
    }

    public class NotificationSendLogItemViewModel
    {
        public long LogId { get; set; }
        public string Channel { get; set; } = "";
        public int? RecipientEmpId { get; set; }
        public string RecipientName { get; set; } = "-";
        public string RecipientAddress { get; set; } = "-";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? TargetUrl { get; set; }
        public DateTime SentAt { get; set; }
    }
}
