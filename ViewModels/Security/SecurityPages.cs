using ProjectTracking.Models;
namespace ProjectTracking.ViewModels.Security;
public class SecurityProjectPage
{
    public Project Project { get; set; }=null!;
    public List<SecuritySystem> Systems { get; set; }=[];
    public List<SecurityVersion> Versions { get; set; }=[];
    public List<SecurityArtifact> Artifacts { get; set; }=[];
    public List<SecurityScan> Scans { get; set; }=[];
    public bool CanManage { get; set; }
    public bool WorkerConfigured { get; set; }
}
public class SecurityScanPage
{
    public SecurityScanProgress? Progress { get; set; }
    public Project Project { get; set; }=null!;
    public SecuritySystem System { get; set; }=null!;
    public SecurityVersion Version { get; set; }=null!;
    public SecurityScan Scan { get; set; }=null!;
    public List<SecurityScanFinding> Observations { get; set; }=[];
    public List<SecurityFinding> Findings { get; set; }=[];
    public List<SecurityFindingHistory> History { get; set; }=[];
    public List<SecurityFindingIssueLink> Links { get; set; }=[];
    public List<Employee> Employees { get; set; }=[];
    public List<ProjectIssue> ExistingIssues { get; set; }=[];
    public List<SecurityScan> Baselines { get; set; }=[];
    public List<SecurityScanFinding>? BaselineFindings { get; set; }
    public bool CanManage { get; set; }
    public bool CanCreateIssue { get; set; }
}
