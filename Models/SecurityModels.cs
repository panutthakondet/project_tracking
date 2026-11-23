using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProjectTracking.Models;

[Table("security_systems")]
public class SecuritySystem
{
    public long Id { get; set; }
    public int ProjectId { get; set; }
    [MaxLength(160)] public string Name { get; set; } = "";
    [MaxLength(24)] public string Kind { get; set; } = "WebInformation";
    [MaxLength(2000)] public string? TargetUrl { get; set; }
    [MaxLength(80)] public string? AuthProfile { get; set; }
    public bool RequiresLogin { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
[Table("security_versions")]
public class SecurityVersion
{
    public long Id { get; set; }
    public long SystemId { get; set; }
    [MaxLength(100)] public string VersionName { get; set; } = "";
    [MaxLength(100)] public string? BuildNumber { get; set; }
    [MaxLength(64)] public string? GitCommit { get; set; }
    [MaxLength(20)] public string Environment { get; set; } = "UAT";
    [MaxLength(4000)] public string? ChangeNotes { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
[Table("security_artifacts")]
public class SecurityArtifact
{
    public long Id { get; set; }
    public long VersionId { get; set; }
    [MaxLength(255)] public string OriginalName { get; set; } = "";
    [MaxLength(100)] public string StorageKey { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}
[Table("security_scans")]
public class SecurityScan
{
    public long Id { get; set; }
    public long VersionId { get; set; }
    [MaxLength(36)] public string RequestKey { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(20)] public string Engine { get; set; } = "ZAP";
    [MaxLength(20)] public string Mode { get; set; } = "Passive";
    [MaxLength(20)] public string Status { get; set; } = "Queued";
    // Immutable execution snapshot: changing a system later must not change queued jobs.
    [MaxLength(2000)] public string? TargetUrl { get; set; }
    [MaxLength(80)] public string? AuthProfile { get; set; }
    public bool RequiresLogin { get; set; }
    [MaxLength(64)] public string? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int CreatedBy { get; set; }
    [MaxLength(200)] public string? EngineVersion { get; set; }
    [MaxLength(2000)] public string? Coverage { get; set; }
    [MaxLength(2000)] public string? Error { get; set; }
}
[Table("security_findings")]
public class SecurityFinding
{
    public long Id { get; set; }
    public long SystemId { get; set; }
    [MaxLength(64)] public string Fingerprint { get; set; } = "";
    [MaxLength(500)] public string Title { get; set; } = "";
    [MaxLength(30)] public string ReviewStatus { get; set; } = "Open";
    public int? AssignedEmpId { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

[Table("security_scan_progress")]
public class SecurityScanProgress
{
    [Key] public long ScanId { get; set; }
    [MaxLength(40)] public string Stage { get; set; } = "Preparing";
    public int? Percent { get; set; }
    public long? BytesDone { get; set; }
    public long? BytesTotal { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
[Table("security_scan_findings")]
public class SecurityScanFinding
{
    public long Id { get; set; }
    public long ScanId { get; set; }
    public long FindingId { get; set; }
    [MaxLength(500)] public string Title { get; set; } = "";
    [MaxLength(20)] public string Severity { get; set; } = "Info";
    [MaxLength(200)] public string RuleId { get; set; } = "";
    [MaxLength(100)] public string? Cwe { get; set; }
    [MaxLength(4000)] public string Location { get; set; } = "";
    [Column(TypeName="longtext")] public string Description { get; set; } = "";
    [Column(TypeName="longtext")] public string Evidence { get; set; } = "";
    [Column(TypeName="longtext")] public string Remediation { get; set; } = "";
    [Column(TypeName="longtext")] public string References { get; set; } = "";
}
[Table("security_reports")]
public class SecurityReport
{
    public long Id { get; set; }
    public long ScanId { get; set; }
    [MaxLength(20)] public string Format { get; set; } = "json";
    [MaxLength(100)] public string StorageKey { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
[Table("security_finding_history")]
public class SecurityFindingHistory
{
    public long Id { get; set; }
    public long FindingId { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Open";
    [MaxLength(4000)] public string Note { get; set; } = "";
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
[Table("security_finding_issue_links")]
public class SecurityFindingIssueLink
{
    public long Id { get; set; }
    public long FindingId { get; set; }
    public int IssueId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("security_host_approvals")]
public class SecurityHostApproval
{
    public long Id { get; set; }
    public int ProjectId { get; set; }
    [MaxLength(253)] public string Host { get; set; } = "";
    public bool IsApproved { get; set; }
    public int UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("security_website_targets")]
public class SecurityWebsiteTarget
{
    public long Id { get; set; }
    public long SystemId { get; set; }
    [MaxLength(24)] public string Section { get; set; } = "WebInfo";
    [MaxLength(2000)] public string TargetUrl { get; set; } = "";
    public bool Enabled { get; set; }
    public bool RequiresLogin { get; set; }
    [MaxLength(80)] public string? AuthProfile { get; set; }
}
[Table("security_website_scan_parts")]
public class SecurityWebsiteScanPart
{
    public long ScanId { get; set; }
    [MaxLength(36)] public string BatchKey { get; set; } = "";
    [MaxLength(24)] public string Section { get; set; } = "";
}

[Table("security_issue_defaults")]
public class SecurityIssueDefaults
{
    [Key] public long SystemId { get; set; }
    public int AssignedEmpId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int CreatedBy { get; set; }
    public int? CreatorEmpId { get; set; }
}
