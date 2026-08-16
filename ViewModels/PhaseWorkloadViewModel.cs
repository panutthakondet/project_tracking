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
        public double PhaseAssignContributionPercent => CompletionRate(PhaseAssignCompleted, PhaseAssignTotal) * 50d;
        public double DevScenarioContributionPercent => CompletionRate(DevScenarioCompleted, DevScenarioTotal) * (50d / 3d);
        public double BaScenarioContributionPercent => CompletionRate(BaScenarioCompleted, BaScenarioTotal) * (50d / 3d);
        public double ProjectIssueContributionPercent => CompletionRate(ProjectIssueCompleted, ProjectIssueTotal) * (50d / 3d);

        public int Percent => Total == 0
            ? 0
            : (int)Math.Round(
                PhaseAssignContributionPercent
                + DevScenarioContributionPercent
                + BaScenarioContributionPercent
                + ProjectIssueContributionPercent,
                MidpointRounding.AwayFromZero);

        private double CompletionRate(int completed, int total)
        {
            if (total <= 0)
            {
                // หมวดที่ไม่มีงานไม่ควรลดเปอร์เซ็นต์รวมของโครงการที่มีงานในหมวดอื่น
                return Total > 0 ? 1d : 0d;
            }

            return Math.Clamp(completed / (double)total, 0d, 1d);
        }
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
