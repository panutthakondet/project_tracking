namespace ProjectTracking.ViewModels
{
    public class LineOverdueOverviewDetailViewModel
    {
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public DateTime Today { get; set; } = DateTime.Today;
        public DateTime RiskUntil { get; set; } = DateTime.Today;
        public int RiskDays { get; set; } = 7;
        public string? CoopName { get; set; }
        public int? ProjectId { get; set; }
        public int? EmpId { get; set; }
        public string? Status { get; set; }
        public int ProjectCount { get; set; }
        public int TotalCount { get; set; }
        public int DoneCount { get; set; }
        public int WarningCount { get; set; }
        public int DangerCount { get; set; }
        public List<ProjectReportOptionViewModel> ProjectOptions { get; set; } = new();
        public List<EmployeeReportOptionViewModel> EmployeeOptions { get; set; } = new();
        public List<LineOverdueOverviewCoopGroupViewModel> CoopGroups { get; set; } = new();
        public List<LineOverdueOverviewAssignViewModel> Rows { get; set; } = new();
    }

    public class LineOverdueOverviewCoopGroupViewModel
    {
        public string CoopName { get; set; } = "-";
        public int ProjectCount { get; set; }
        public int TotalCount { get; set; }
        public int DoneCount { get; set; }
        public int WarningCount { get; set; }
        public int DangerCount { get; set; }
        public List<LineOverdueOverviewAssignViewModel> Rows { get; set; } = new();
    }

    public class LineOverdueOverviewAssignViewModel
    {
        public int AssignId { get; set; }
        public int ProjectId { get; set; }
        public int EmpId { get; set; }
        public string CoopName { get; set; } = "-";
        public string ProjectName { get; set; } = "-";
        public string PhaseName { get; set; } = "-";
        public string PhasePeriodLabel { get; set; } = "-";
        public string Role { get; set; } = "-";
        public string OwnerName { get; set; } = "-";
        public string OwnerAvatarPath { get; set; } = "/images/Profile/profile.png";
        public string BaName { get; set; } = "-";
        public string BaAvatarPath { get; set; } = "/images/Profile/profile.png";
        public string StatusCategory { get; set; } = "";
        public string StatusText { get; set; } = "-";
        public string StatusTone { get; set; } = "warning";
        public DateTime? PlanStart { get; set; }
        public DateTime? PlanEnd { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public int OverdueDays { get; set; }
        public string Remark { get; set; } = "-";
    }
}
