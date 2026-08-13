using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models;

[Table("project_tor_items")]
public class ProjectTorItem
{
    [Key, Column("tor_item_id")] public int TorItemId { get; set; }
    [Column("project_id")] public int ProjectId { get; set; }
    [Column("tor_code"), MaxLength(50)] public string TorCode { get; set; } = string.Empty;
    [Column("title"), MaxLength(500)] public string Title { get; set; } = string.Empty;
    [Column("detail", TypeName = "text")] public string? Detail { get; set; }
    [Column("acceptance_criteria", TypeName = "text")] public string? AcceptanceCriteria { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    [Column("created_by_emp_id")] public int? CreatedByEmpId { get; set; }
    public Project? Project { get; set; }
}

[Table("phase_assign_tor_items")]
public class PhaseAssignTorItem
{
    [Column("assign_id")] public int AssignId { get; set; }
    [Column("tor_item_id")] public int TorItemId { get; set; }
    [Column("check_status"), MaxLength(20)] public string CheckStatus { get; set; } = "PENDING";
    [Column("checked_by_emp_id")] public int? CheckedByEmpId { get; set; }
    [Column("checked_at")] public DateTime? CheckedAt { get; set; }
    [Column("remark"), MaxLength(1000)] public string? Remark { get; set; }
    public PhaseAssign? Assignment { get; set; }
    public ProjectTorItem? TorItem { get; set; }
}

[Table("phase_assign_test_scenarios")]
public class PhaseAssignTestScenario
{
    [Column("assign_id")] public int AssignId { get; set; }
    [Column("scenario_id")] public int ScenarioId { get; set; }
    [Column("is_required")] public bool IsRequired { get; set; } = true;
    public PhaseAssign? Assignment { get; set; }
    public TestScenario? Scenario { get; set; }
}

[Table("test_scenario_runs")]
public class TestScenarioRun
{
    [Key, Column("run_id")] public int RunId { get; set; }
    [Column("assign_id")] public int AssignId { get; set; }
    [Column("scenario_id")] public int ScenarioId { get; set; }
    [Column("test_stage"), MaxLength(10)] public string TestStage { get; set; } = "DEV";
    [Column("round_no")] public int RoundNo { get; set; } = 1;
    [Column("result_status"), MaxLength(20)] public string ResultStatus { get; set; } = "READY";
    [Column("tested_by_emp_id")] public int? TestedByEmpId { get; set; }
    [Column("tested_at")] public DateTime? TestedAt { get; set; }
    [Column("remark"), MaxLength(2000)] public string? Remark { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    public PhaseAssign? Assignment { get; set; }
    public TestScenario? Scenario { get; set; }
    public Employee? TestedBy { get; set; }
}
