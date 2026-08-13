namespace ProjectTracking.Models;

public class DevelopmentWorkflowViewModel
{
    public PhaseAssign Assignment { get; set; } = new();
    public ProjectPhase Phase { get; set; } = new();
    public Project Project { get; set; } = new();
    public Employee Employee { get; set; } = null!;
    public List<DevelopmentTorRow> TorItems { get; set; } = new();
    public List<DevelopmentScenarioRow> Scenarios { get; set; } = new();
    public int TorPercent { get; set; }
    public int DevPercent { get; set; }
    public int BaPercent { get; set; }
    public int TotalPercent { get; set; }
    public bool CanWorkAsDev { get; set; }
}

public class DevelopmentTorRow
{
    public ProjectTorItem Item { get; set; } = new();
    public string Status { get; set; } = "PENDING";
    public string? Remark { get; set; }
    public DateTime? CheckedAt { get; set; }
}

public class DevelopmentScenarioRow
{
    public TestScenario Scenario { get; set; } = new();
    public TestScenarioRun? DevRun { get; set; }
    public TestScenarioRun? BaRun { get; set; }
}

public class DevelopmentQueueRow
{
    public int AssignId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string PhaseName { get; set; } = string.Empty;
    public string WorkName { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string WorkflowStatus { get; set; } = string.Empty;
    public int Progress { get; set; }
}
