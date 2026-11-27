namespace ProjectTracking.Models;

public sealed class ProjectSyncRun
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ComparedAtUtc { get; set; }
    public string StructureJson { get; set; } = "";
    public string? PlanJson { get; set; }
    public string? ResultJson { get; set; }
}
