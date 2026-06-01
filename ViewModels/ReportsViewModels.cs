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
}
