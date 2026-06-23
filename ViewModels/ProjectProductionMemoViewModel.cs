namespace ProjectTracking.ViewModels
{
    public class ProjectProductionMemoViewModel
    {
        public int ProjectId { get; set; }
        public string CoopName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string ProjectDisplayName { get; set; } = "";
        public string? LinkName { get; set; }
        public string? DatabaseName { get; set; }
        public string? TestAccount { get; set; }
        public string? RemoteUrl { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string BusinessAnalystName { get; set; } = "";
        public List<ProjectProductionMemoPhaseViewModel> Phases { get; set; } = new();
        public List<ProjectProductionMemoOwnerViewModel> Owners { get; set; } = new();
    }

    public class ProjectProductionMemoPhaseViewModel
    {
        public int PhaseOrder { get; set; }
        public int PeriodOrder { get; set; }
        public int PeriodTotal { get; set; }
        public string PhaseName { get; set; } = "";
        public DateTime? PlanStart { get; set; }
        public DateTime? PlanEnd { get; set; }
        public DateTime? PeriodEndDate { get; set; }
        public int? DurationDays { get; set; }
    }

    public class ProjectProductionMemoOwnerViewModel
    {
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
    }
}
