using System;
using System.Collections.Generic;

namespace ProjectTracking.ViewModels
{
    public class PhaseWorkloadViewModel
    {
        public List<PhaseWorkloadItemViewModel> Items { get; set; } = new();
    }

    public class PhaseWorkloadItemViewModel
    {
        public string WorkType { get; set; } = "";
        public string WorkTypeLabel { get; set; } = "";
        public string WorkTypeClass { get; set; } = "";
        public int ItemId { get; set; }
        public int EmpId { get; set; }
        public string EmpName { get; set; } = "";
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public int PhaseSort { get; set; }
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
