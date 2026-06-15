using System;
using System.Collections.Generic;

namespace ProjectTracking.ViewModels
{
    public class ReportCenterViewModel
    {
        public DateTime GeneratedAt { get; set; }
        public List<ReportCardViewModel> Reports { get; set; } = new();
    }

    public class ReportCardViewModel
    {
        public string Group { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "";
        public string? PermissionKey { get; set; }
        public string Icon { get; set; } = "";
        public string Tone { get; set; } = "";
        public bool IsPrimary { get; set; }
    }

    public class ExecutiveReportViewModel
    {
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        public List<ExecutiveKpiViewModel> Kpis { get; set; } = new();
        public List<ExecutiveRiskProjectViewModel> RiskProjects { get; set; } = new();
        public List<ExecutiveDueItemViewModel> DueItems { get; set; } = new();
        public List<ExecutiveAgingItemViewModel> AgingItems { get; set; } = new();
        public List<ExecutiveWorkloadRowViewModel> TeamWorkload { get; set; } = new();
    }

    public class ExecutiveKpiViewModel
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string Note { get; set; } = "";
        public string Tone { get; set; } = "";
    }

    public class ExecutiveRiskProjectViewModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string RiskLevel { get; set; } = "";
        public string RiskTone { get; set; } = "";
        public int RiskScore { get; set; }
        public int Progress { get; set; }
        public string DueText { get; set; } = "";
        public int OpenIssues { get; set; }
        public int UrgentIssues { get; set; }
        public int OverduePhases { get; set; }
        public int OverdueAssigns { get; set; }
        public int OpenSupportOrders { get; set; }
        public int OverdueFollowups { get; set; }
        public List<string> Reasons { get; set; } = new();
    }

    public class ExecutiveDueItemViewModel
    {
        public string Type { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Title { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public DateTime? DueDate { get; set; }
        public int OverdueDays { get; set; }
        public string Status { get; set; } = "";
        public string Tone { get; set; } = "";
    }

    public class ExecutiveAgingItemViewModel
    {
        public string Type { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Title { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public int AgeDays { get; set; }
        public string Tone { get; set; } = "";
    }

    public class ExecutiveWorkloadRowViewModel
    {
        public string EmployeeName { get; set; } = "";
        public int Assignments { get; set; }
        public int Issues { get; set; }
        public int SupportOrders { get; set; }
        public int Followups { get; set; }
        public int Total { get; set; }
        public int Percent { get; set; }
    }

    public class TaskProgressReportViewModel
    {
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        public int Year { get; set; }
        public int? ProjectId { get; set; }
        public int? EmpId { get; set; }
        public string Status { get; set; } = "";
        public List<int> YearOptions { get; set; } = new();
        public List<ProjectReportOptionViewModel> ProjectOptions { get; set; } = new();
        public List<EmployeeReportOptionViewModel> EmployeeOptions { get; set; } = new();
        public TaskProgressSummaryViewModel Summary { get; set; } = new();
        public List<TaskProgressMonthViewModel> Months { get; set; } = new();
        public List<TaskProgressReportRowViewModel> Rows { get; set; } = new();
    }

    public class TaskProgressSummaryViewModel
    {
        public int Total { get; set; }
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Pending { get; set; }
        public int Projects { get; set; }
        public int Employees { get; set; }
    }

    public class TaskProgressMonthViewModel
    {
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Pending { get; set; }
        public int Total => Completed + InProgress + Pending;
    }

    public class TaskProgressReportRowViewModel
    {
        public int Seq { get; set; }
        public int AssignId { get; set; }
        public int ProjectId { get; set; }
        public int EmpId { get; set; }
        public string ProjectName { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string PhaseName { get; set; } = "";
        public string PhasePeriodLabel { get; set; } = "";
        public string Role { get; set; } = "";
        public string StatusCategory { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string StatusTone { get; set; } = "";
        public DateTime? PlanStart { get; set; }
        public DateTime? PlanEnd { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public DateTime? BucketDate { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
    }

    public class ProjectReportOptionViewModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string CoopName { get; set; } = "";
    }

    public class EmployeeReportOptionViewModel
    {
        public int EmpId { get; set; }
        public string EmpName { get; set; } = "";
    }

    public class MeetingReportRowViewModel
    {
        public int Id { get; set; }
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Location { get; set; } = "";
        public string Audience { get; set; } = "";
        public string CreatedByName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<MeetingReportAttendeeViewModel> Attendees { get; set; } = new();
    }

    public class MeetingReportAttendeeViewModel
    {
        public int EmpId { get; set; }
        public string EmpName { get; set; } = "";
        public string Position { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
