using System;
using System.Collections.Generic;

namespace ProjectTracking.ViewModels
{
    public class PhaseWorkloadViewModel
    {
        public List<PhaseWorkloadItemViewModel> Items { get; set; } = new();
        public PhaseWorkloadCompletionViewModel Completion { get; set; } = new();
        public PhaseWorkloadIssueSummaryViewModel Issues { get; set; } = new();
    }

    public class PhaseWorkloadIssueSummaryViewModel
    {
        public int Open { get; set; }
        public int Fail { get; set; }
        public int Pass { get; set; }
        public int Reject { get; set; }

        public int Total => Open + Fail + Pass + Reject;
    }

    public class PhaseWorkloadCompletionViewModel
    {
        public int PhaseAssignTotal { get; set; }
        public int PhaseAssignCompleted { get; set; }
        public int DevScenarioTotal { get; set; }
        public int DevScenarioCompleted { get; set; }
        public int BaScenarioTotal { get; set; }
        public int BaScenarioCompleted { get; set; }
        public int ProjectIssueTotal { get; set; }
        public int ProjectIssueCompleted { get; set; }

        public int Total => PhaseAssignTotal + DevScenarioTotal + BaScenarioTotal + ProjectIssueTotal;
        public int Completed => PhaseAssignCompleted + DevScenarioCompleted + BaScenarioCompleted + ProjectIssueCompleted;
        public int Percent => Total == 0
            ? 0
            : (int)Math.Round(Completed * 100d / Total, MidpointRounding.AwayFromZero);
    }

    public class PhaseWorkloadItemViewModel
    {
        public string WorkType { get; set; } = "";
        public string WorkTypeLabel { get; set; } = "";
        public string WorkTypeClass { get; set; } = "";
        public int ItemId { get; set; }
        public int EmpId { get; set; }
        public string EmpName { get; set; } = "";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public int? PhaseSort { get; set; }
        public int PhaseOrder { get; set; }
        public int PeriodOrder { get; set; }
        public string PhasePeriodLabel { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public DateTime? AssignStartDate { get; set; }
        public DateTime? AssignEndDate { get; set; }
        public DateTime? PhasePlanStartDate { get; set; }
        public DateTime? PhasePlanEndDate { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? PeriodEndDate { get; set; }
        public string Status { get; set; } = "";
        public string WorkState { get; set; } = "";
        public string Url { get; set; } = "";
        public int SortOrder { get; set; }
    }
}
