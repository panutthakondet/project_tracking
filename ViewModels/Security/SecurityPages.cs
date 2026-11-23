using ProjectTracking.Models;
namespace ProjectTracking.ViewModels.Security;
public class SecurityProjectPage
{
    public string Workflow { get; set; }="Formal";
    public List<SecuritySystem> HistorySystems { get; set; }=[];
    public List<SecurityVersion> HistoryVersions { get; set; }=[];
    public bool CanAutoCreateIssues { get; set; }
    public List<SecurityIssueDefaults> IssueDefaults { get; set; }=[];
    public List<Employee> ResponsibleEmployees { get; set; }=[];
    public Project Project { get; set; }=null!;
    public List<SecurityWebsiteTarget> WebsiteTargets { get; set; }=[];
    public List<SecurityWebsiteScanPart> WebsiteScanParts { get; set; }=[];
    public Dictionary<long,SecurityLoginSettings> WebsiteLoginSettings { get; set; }=[];
    public HashSet<long> UnreadableWebsiteLogins { get; set; }=[];
    public bool CanDeleteScans { get; set; }
    public bool CanApproveHosts { get; set; }
    public List<SecurityHostApproval> HostApprovals { get; set; }=[];
    public List<SecuritySystem> Systems { get; set; }=[];
    public List<SecurityVersion> Versions { get; set; }=[];
    public List<SecurityArtifact> Artifacts { get; set; }=[];
    public List<SecurityScan> Scans { get; set; }=[];
    public bool CanManage { get; set; }
    public bool WorkerConfigured { get; set; }
    public Dictionary<long,SecurityLoginSettings> LoginSettings { get; set; }=[];
    public HashSet<long> UnreadableLoginSettings { get; set; }=[];
}
public class SecurityScanPage
{
    public SecurityWebsiteScanPart? WebsitePart { get; set; }
    public List<SecurityWebsiteScanPart> WebsiteBatchParts { get; set; }=[];
    public int TotalObservations { get; set; }
    public int PageNumber { get; set; }=1;
    public int PageSize { get; set; }=50;
    public int PageCount => Math.Max(1,(int)Math.Ceiling(TotalObservations/(double)PageSize));
    public long? BaselineId { get; set; }
    public Dictionary<string,int> SeverityCounts { get; set; }=[];
    public List<long> AllFindingIds { get; set; }=[];
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
    public List<ProjectIssue> LinkedIssues { get; set; }=[];
    public List<ProjectIssue> ExistingIssues { get; set; }=[];
    public List<SecurityScan> Baselines { get; set; }=[];
    public List<SecurityScanFinding>? BaselineFindings { get; set; }
    public bool CanManage { get; set; }
    public bool CanCreateIssue { get; set; }
}

// Deliberately excludes Password: this is the only login data sent to Razor.
public sealed class SecurityLoginSettings
{
    public string AuthenticationType { get; set; }="Form";
    public string TokenPath { get; set; }="token";
    public string? ScanStartUrl { get; set; }
    public string LoginUrl { get; set; }="";
    public string Username { get; set; }="";
    public string LoggedInRegex { get; set; }="";
    public string RequestData { get; set; }="";
}

public class SecurityWebsiteTargetForm
{
    public string Workflow { get; set; }="Formal";
    public SecuritySystem System { get; set; }=null!;
    public SecurityWebsiteTarget Target { get; set; }=null!;
    public SecurityLoginSettings? Login { get; set; }
    public bool CanManage { get; set; }
    public bool CanApprove { get; set; }
    public bool Approved { get; set; }
    public bool Unreadable { get; set; }
}
