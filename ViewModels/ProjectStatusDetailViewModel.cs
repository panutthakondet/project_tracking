using System;
using System.Collections.Generic;

namespace ProjectTracking.ViewModels
{
    public class ProjectStatusDetailViewModel
    {
        public int? SelectedProjectId { get; set; }
        public string SelectedProjectName { get; set; } = "ทุกโครงการ";
        public List<ProjectStatusOption> ProjectOptions { get; set; } = new();
        public int? SelectedDepartmentId { get; set; }
        public List<ProjectDepartmentOption> DepartmentOptions { get; set; } = new();
        public int TotalProjects { get; set; }
        public int DoneProjects { get; set; }
        public int InProgressProjects { get; set; }
        public int PlanProjects { get; set; }
        public int DelayedProjects { get; set; }
        public string WeekRangeText { get; set; } = string.Empty;
        public string ProjectStatusChart { get; set; } = "conic-gradient(#e5e7eb 0 100%)";
        public List<ProjectStatusMetric> StatusMetrics { get; set; } = new();
        public List<ProjectTaskOverviewMember> TaskOverview { get; set; } = new();
        public ProjectOrgMember Lead { get; set; } = new();
        public List<ProjectOrgMember> OrgManagers { get; set; } = new();
        public List<ProjectOrgMember> OrgTeam { get; set; } = new();
        public List<ProjectTeamGroup> TeamGroups { get; set; } = new();
        public List<ProjectWeekTask> ThisWeekTasks { get; set; } = new();
    }

    public class ProjectStatusOption
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
    }

    public class ProjectDepartmentOption
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
    }

    public class ProjectStatusMetric
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Percent { get; set; }
        public string Color { get; set; } = "#94a3b8";
    }

    public class ProjectTaskOverviewMember
    {
        public int EmpId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public int DoneCount { get; set; }
        public int InProgressCount { get; set; }
        public int DelayCount { get; set; }
        public int OpenIssueCount { get; set; }
        public int OpenSupportCount { get; set; }
        public int FieldServiceCount { get; set; }
        public int TotalCount { get; set; }
        public int TotalHeightPercent { get; set; }
        public int DoneHeightPercent { get; set; }
        public int InProgressHeightPercent { get; set; }
        public int DelayHeightPercent { get; set; }
        public int OpenIssueHeightPercent { get; set; }
        public int OpenSupportHeightPercent { get; set; }
        public int FieldServiceHeightPercent { get; set; }
    }

    public class ProjectOrgMember
    {
        public int EmpId { get; set; }
        public string Name { get; set; } = "-";
        public string Role { get; set; } = "Team Member";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public int WorkCount { get; set; }
    }

    public class ProjectTeamGroup
    {
        public string Label { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public List<ProjectOrgMember> Members { get; set; } = new();
    }

    public class ProjectWeekTask
    {
        public int AssignId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public string DueDateText { get; set; } = "-";
        public DateTime? DueDate { get; set; }
        public string StatusText { get; set; } = "Working on it";
        public string StatusClass { get; set; } = "working";
    }
}
