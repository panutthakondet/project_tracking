using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_status")]
    public sealed class ProjectStatusDefinition
    {
        [Key, Column("status_id")] public int StatusId { get; set; }
        [Required, StringLength(50), Column("status_code")] public string StatusCode { get; set; } = string.Empty;
        [Required, StringLength(100), Column("status_desc")] public string StatusDesc { get; set; } = string.Empty;
        [Column("sort_order")] public int SortOrder { get; set; }
        [Column("is_active")] public bool IsActive { get; set; } = true;
    }

    [Table("project_phase_status")]
    public sealed class ProjectPhaseStatusDefinition
    {
        [Key, Column("status_id")] public int StatusId { get; set; }
        [Required, StringLength(50), Column("status_code")] public string StatusCode { get; set; } = string.Empty;
        [Required, StringLength(100), Column("status_desc")] public string StatusDesc { get; set; } = string.Empty;
        [Column("sort_order")] public int SortOrder { get; set; }
        [Column("is_active")] public bool IsActive { get; set; } = true;
    }

    [Table("phase_assign_status")]
    public sealed class PhaseAssignStatusDefinition
    {
        [Key, Column("status_id")] public int StatusId { get; set; }
        [Required, StringLength(50), Column("status_code")] public string StatusCode { get; set; } = string.Empty;
        [Required, StringLength(100), Column("status_desc")] public string StatusDesc { get; set; } = string.Empty;
        [Column("sort_order")] public int SortOrder { get; set; }
        [Column("is_active")] public bool IsActive { get; set; } = true;
    }

    public static class WorkflowStatusTypes
    {
        public const string Project = "PROJECT";
        public const string ProjectPhase = "PROJECT_PHASE";
        public const string PhaseAssign = "PHASE_ASSIGN";
    }

    public sealed class StatusDefinitionOption
    {
        public int StatusId { get; init; }
        public string StatusCode { get; init; } = string.Empty;
        public string StatusDesc { get; init; } = string.Empty;
        public int SortOrder { get; init; }
    }
}
