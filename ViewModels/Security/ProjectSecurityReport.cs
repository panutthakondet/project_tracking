using ProjectTracking.Services.Security;

namespace ProjectTracking.ViewModels.Security;

public sealed record ProjectSecurityReport(int ProjectId, string ProjectName, IReadOnlyList<ProjectSecurityReportGroup> Groups);

public sealed record ProjectSecurityReportGroup(string Category, SecurityScanRun? Run, IReadOnlyList<ProjectSecurityReportFinding> Findings);

public sealed record ProjectSecurityReportFinding(string Title, string Severity, string Section, string Status);
