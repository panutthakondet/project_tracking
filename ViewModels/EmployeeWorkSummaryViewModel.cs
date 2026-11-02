namespace ProjectTracking.ViewModels;

public class EmployeeWorkSummaryViewModel
{
    public int EmpId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public DateTime Today { get; set; }
    public int RiskDays { get; set; }
    public string Filter { get; set; } = "all";
    public List<EmployeeWorkSummaryItem> Items { get; set; } = new();
    public int DoneCount => Items.Count(x => x.IsDone);
    public int PendingCount => Items.Count(x => !x.IsDone);
    public int RiskCount => Items.Count(x => x.Category(Today, RiskDays) == "risk");
    public int OverdueCount => Items.Count(x => x.Category(Today, RiskDays) == "overdue");
    public IEnumerable<EmployeeWorkSummaryItem> VisibleItems => Items.Where(x =>
        Filter == "all" || (Filter == "pending" ? !x.IsDone : x.Category(Today, RiskDays) == Filter));
}

public class EmployeeWorkSummaryItem
{
    public string Type { get; set; } = "";
    public int? CoopId { get; set; }
    public string CoopName { get; set; } = "ไม่ระบุสหกรณ์";
    public int? ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public bool IsDone { get; set; }
    public string Url { get; set; } = "";

    public string Category(DateTime today, int riskDays)
    {
        if (IsDone) return "done";
        if (!DueDate.HasValue) return "pending";
        if (DueDate.Value.Date < today.Date) return "overdue";
        return DueDate.Value.Date <= today.Date.AddDays(riskDays) ? "risk" : "pending";
    }
}
