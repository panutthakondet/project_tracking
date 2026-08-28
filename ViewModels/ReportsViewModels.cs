using System;
using System.Collections.Generic;
using ProjectTracking.Models;

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
        public int? DepartmentId { get; set; }
        public List<DepartmentReportOptionViewModel> DepartmentOptions { get; set; } = new();
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
        public int? DepartmentId { get; set; }
        public List<DepartmentReportOptionViewModel> DepartmentOptions { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        public int Year { get; set; }
        public int? ProjectId { get; set; }
        public int? EmpId { get; set; }
        public int? BaEmpId { get; set; }
        public string Status { get; set; } = "";
        public string AssignStatus { get; set; } = "";
        public List<int> YearOptions { get; set; } = new();
        public List<ProjectReportOptionViewModel> ProjectOptions { get; set; } = new();
        public List<EmployeeReportOptionViewModel> EmployeeOptions { get; set; } = new();
        public List<EmployeeReportOptionViewModel> BaOptions { get; set; } = new();
        public List<string> AssignStatusOptions { get; set; } = new();
        public List<StatusDefinitionOption> PhaseStatusDefinitions { get; set; } = new();
        public List<StatusDefinitionOption> AssignStatusDefinitions { get; set; } = new();
        public TaskProgressSummaryViewModel Summary { get; set; } = new();
        public List<TaskProgressMonthViewModel> Months { get; set; } = new();
        public List<TaskProgressReportRowViewModel> Rows { get; set; } = new();
    }

    public class TaskProgressSummaryViewModel
    {
        public int Total { get; set; }
        public int Projects { get; set; }
        public int Employees { get; set; }
        public List<TaskProgressStatusCountViewModel> StatusCounts { get; set; } = new();
    }

    public class TaskProgressMonthViewModel
    {
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public int Total { get; set; }
        public List<TaskProgressStatusCountViewModel> StatusCounts { get; set; } = new();
    }

    public class TaskProgressStatusCountViewModel
    {
        public string StatusCode { get; set; } = "";
        public string StatusDesc { get; set; } = "";
        public string Tone { get; set; } = "muted";
        public int SortOrder { get; set; }
        public int Count { get; set; }
    }

    public class TaskProgressReportRowViewModel
    {
        public int Seq { get; set; }
        public int AssignId { get; set; }
        public int ProjectId { get; set; }
        public int EmpId { get; set; }
        public int? BaEmpId { get; set; }
        public List<int> BaEmpIds { get; set; } = new();
        public string ProjectName { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string PhaseName { get; set; } = "";
        public string PhasePeriodLabel { get; set; } = "";
        public string Role { get; set; } = "";
        public string PhaseStatusCode { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string StatusTone { get; set; } = "";
        public string AssignStatus { get; set; } = "";
        public string AssignStatusText { get; set; } = "";
        public string AssignStatusTone { get; set; } = "";
        public DateTime? PlanStart { get; set; }
        public DateTime? PlanEnd { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public DateTime? BucketDate { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
    }

    public class PendingWorkReportViewModel
    {
        public int? DepartmentId { get; set; }
        public List<DepartmentReportOptionViewModel> DepartmentOptions { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        public DateTime Today { get; set; }
        public DateTime HorizonDate { get; set; }
        public int? ProjectId { get; set; }
        public int? EmpId { get; set; }
        public int? BaEmpId { get; set; }
        public string WorkType { get; set; } = "";
        public string Section { get; set; } = "";
        public List<ProjectReportOptionViewModel> ProjectOptions { get; set; } = new();
        public List<EmployeeReportOptionViewModel> EmployeeOptions { get; set; } = new();
        public List<EmployeeReportOptionViewModel> BaOptions { get; set; } = new();
        public PendingWorkSummaryViewModel Summary { get; set; } = new();
        public List<PendingWorkReportRowViewModel> Rows { get; set; } = new();
    }

    public class PendingWorkSummaryViewModel
    {
        public int Total { get; set; }
        public int Overdue { get; set; }
        public int Upcoming { get; set; }
        public int Projects { get; set; }
        public int Owners { get; set; }
        public int Assigns { get; set; }
        public int Issues { get; set; }
        public int SupportOrders { get; set; }
    }

    public class PendingWorkReportRowViewModel
    {
        public int Seq { get; set; }
        public string Section { get; set; } = "";
        public string SectionText { get; set; } = "";
        public string Tone { get; set; } = "";
        public string WorkType { get; set; } = "";
        public string WorkTypeText { get; set; } = "";
        public int? ProjectId { get; set; }
        public string CoopName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public int? OwnerEmpId { get; set; }
        public string OwnerName { get; set; } = "";
        public int? BaEmpId { get; set; }
        public List<int> BaEmpIds { get; set; } = new();
        public string BaName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Priority { get; set; } = "";
        public DateTime? StartDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? PeriodEndDate { get; set; }
        public int OverdueDays { get; set; }
        public int DaysUntilDue { get; set; }
        public string TargetUrl { get; set; } = "";
    }

    public class ProjectReportOptionViewModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string CoopName { get; set; } = "";
    }

    public class DepartmentReportOptionViewModel
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "";
    }

    public class EmployeeReportOptionViewModel
    {
        public int EmpId { get; set; }
        public string EmpName { get; set; } = "";
    }

    public class WorkDurationReportViewModel
    {
        public int DepartmentFilterValue { get; set; }
        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "ทุกฝ่าย";
        public int? EmpId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        public List<DepartmentReportOptionViewModel> DepartmentOptions { get; set; } = new();
        public List<EmployeeReportOptionViewModel> EmployeeOptions { get; set; } = new();
        public List<StatusDefinitionOption> AssignStatusDefinitions { get; set; } = new();
        public WorkDurationSummaryViewModel Summary { get; set; } = new();
        public List<WorkDurationEmployeeViewModel> Employees { get; set; } = new();
        public List<WorkDurationTaskViewModel> Tasks { get; set; } = new();
    }

    public class WorkDurationSummaryViewModel
    {
        public int TotalProjects { get; set; }
        public int Total { get; set; }
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Planned { get; set; }
        public int Overdue { get; set; }
        public int CompletionPercent { get; set; }
        public decimal AveragePlanDays { get; set; }
        public decimal AverageActualDays { get; set; }
        public int TotalVarianceDays { get; set; }
    }

    public class WorkDurationEmployeeViewModel
    {
        public int EmpId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string Position { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public int Total { get; set; }
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Planned { get; set; }
        public int Overdue { get; set; }
        public int PlanDays { get; set; }
        public int ActualDays { get; set; }
        public int VarianceDays { get; set; }
        public int CompletionPercent { get; set; }
    }

    public class WorkDurationTaskViewModel
    {
        public int AssignId { get; set; }
        public int ProjectId { get; set; }
        public int EmpId { get; set; }
        public int? PhaseOrder { get; set; }
        public int? PeriodOrder { get; set; }
        public int? PhaseSort { get; set; }
        public string EmployeeName { get; set; } = "";
        public string Position { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public string CoopName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string PeriodName { get; set; } = "";
        public string WorkName { get; set; } = "";
        public DateTime? PlanStart { get; set; }
        public DateTime? PlanEnd { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualEnd { get; set; }
        public int PlanDays { get; set; }
        public int ActualDays { get; set; }
        public int VarianceDays { get; set; }
        public string WorkflowStatusCode { get; set; } = "";
        public string StatusCode { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string StatusColor { get; set; } = "#8291a7";
        public string ScheduleText { get; set; } = "";
        public string ScheduleTone { get; set; } = "";
        public bool HasStarted { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsOverdue { get; set; }
        public bool IsCompletedLate { get; set; }
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
