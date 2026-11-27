using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.ViewModels.Security;

namespace ProjectTracking.Services.Security;

public static class ProjectSecurityReportReader
{
    public static async Task<int?> FindEmployeeIdAsync(AppDbContext db, int userId, CancellationToken ct) =>
        await db.LoginUsers.Where(user => user.UserId == userId).Select(user => user.EmpId).FirstOrDefaultAsync(ct)
        ?? await db.Employees.Where(employee => employee.LoginUserId == userId)
            .Select(employee => (int?)employee.EmpId).FirstOrDefaultAsync(ct);

    public static async Task<ProjectSecurityReport> ReadAsync(AppDbContext db, Project project, CancellationToken ct, SecurityScanRun? selectedRun = null)
    {
        var projectId = project.ProjectId;
        var workflow = selectedRun?.Scan.Workflow ?? "Formal";
        var runs = await (from scan in db.SecurityScanRows.AsNoTracking()
                          join version in db.SecurityVersionRows on scan.VersionId equals version.Id
                          join system in db.SecuritySystemRows on version.SystemId equals system.Id
                          where system.ProjectId == projectId && scan.Workflow == workflow
                          select new { Scan = scan, system.Kind, version.SystemId, version.Environment }).ToListAsync(ct);
        var scanIds = runs.Select(row => row.Scan.Id).ToArray();
        var parts = await db.SecurityWebsiteScanParts.AsNoTracking()
            .Where(part => scanIds.Contains(part.ScanId)).ToListAsync(ct);
        var scopeByScan = runs.ToDictionary(row => row.Scan.Id, row => (row.SystemId, row.Environment));
        var comparisons = runs.Where(row => selectedRun == null || row.SystemId == scopeByScan[selectedRun.Scan.Id].SystemId)
            .GroupBy(row => SecurityValues.Category(row.Kind))
            .ToDictionary(group => group.Key, group =>
            {
                var ordered = SecurityScanRun.Group(group.Select(row => row.Scan), parts)
                    .OrderByDescending(run => run.Scan.CreatedAt)
                    .ThenByDescending(run => run.Scan.Id).ToList();
                var latest = selectedRun ?? ordered[0];
                var previous = latest.Scan.Status == "Completed" ? ordered.SkipWhile(run => run.Scan.Id != latest.Scan.Id).Skip(1).FirstOrDefault(run =>
                    run.Scan.Status == "Completed" && scopeByScan[run.Scan.Id] == scopeByScan[latest.Scan.Id] &&
                    run.Scan.Engine == latest.Scan.Engine && run.Scan.Mode == latest.Scan.Mode &&
                    run.ComparisonKey() == latest.ComparisonKey()) : null;
                return (Latest: latest, Previous: previous);
            });
        var completedIds = comparisons.Values.SelectMany(pair => pair.Latest.Scans
                .Concat(pair.Previous?.Scans ?? Array.Empty<SecurityScan>()))
            .Where(scan => scan.Status == "Completed").Select(scan => scan.Id).ToArray();

        var includeDetails = selectedRun != null;
        var findings = await (from observation in db.SecurityScanFindingRows.AsNoTracking()
                              join finding in db.SecurityFindingRows on observation.FindingId equals finding.Id
                              where completedIds.Contains(observation.ScanId)
                              select new
                              {
                                  observation.ScanId, observation.FindingId, observation.Title,
                                  observation.Severity, finding.ReviewStatus,
                                  observation.Location, observation.Cwe, observation.Description,
                                  Evidence = includeDetails ? observation.Evidence : null,
                                  Remediation = includeDetails ? observation.Remediation : null,
                                  References = includeDetails ? observation.References : null,
                                  LatestIssueId = (from link in db.SecurityFindingIssueLinkRows
                                                 join issue in db.ProjectIssues on link.IssueId equals issue.IssueId
                                                 where link.FindingId == finding.Id && issue.ProjectId == projectId
                                                 orderby link.Id descending
                                                 select (int?)issue.IssueId).FirstOrDefault()
                              }).ToListAsync(ct);

        var issueIds = findings.Select(item => item.LatestIssueId).OfType<int>().Distinct().ToArray();
        var issues = await db.ProjectIssues.AsNoTracking()
            .Where(issue => issue.ProjectId == projectId && issueIds.Contains(issue.IssueId))
            .ToDictionaryAsync(issue => issue.IssueId, ct);

        var groups = SecurityValues.Kinds.Where(category => selectedRun == null || comparisons.ContainsKey(category)).Select(category =>
        {
            comparisons.TryGetValue(category, out var comparison);
            var run = comparison.Latest;
            var previous = comparison.Previous;
            var previousIds = previous?.Ids.ToHashSet();
            var previousKeys = previous == null ? null : findings
                .Where(item => previousIds!.Contains(item.ScanId))
                .Select(item => (item.FindingId, Section: previous.Section(item.ScanId))).ToHashSet();
            var ids = run?.Ids.ToHashSet() ?? new HashSet<long>();
            var items = findings.Where(item => ids.Contains(item.ScanId))
                .OrderByDescending(item => SecurityValues.Rank(item.Severity))
                .ThenBy(item => item.Title)
                .GroupBy(item => (item.FindingId, Section: run!.Section(item.ScanId)))
                .Select(group =>
                {
                    var item = group.First();
                    var status = item.LatestIssueId is int issueId && issues.TryGetValue(issueId, out var issue)
                        ? SecurityIssueWorkflow.CurrentReviewStatus(issue) : item.ReviewStatus;
                    return new ProjectSecurityReportFinding(item.Title, item.Severity,
                        SecurityValues.SectionLabel(group.Key.Section), status,
                        previousKeys == null ? null : !previousKeys.Contains(group.Key),
                        item.Location, item.Cwe, item.Description)
                    {
                        Evidence = item.Evidence,
                        Remediation = item.Remediation,
                        References = item.References
                    };
                })
                .ToList();
            var currentKeys = findings.Where(item => ids.Contains(item.ScanId))
                .Select(item => (item.FindingId, Section: run!.Section(item.ScanId))).ToHashSet();
            return new ProjectSecurityReportGroup(category, run, items, previous?.Scan.Id,
                previousKeys?.Count(key => !currentKeys.Contains(key)) ?? 0);
        }).ToList();

        return new ProjectSecurityReport(projectId, project.ProjectDisplayName, groups);
    }
}
