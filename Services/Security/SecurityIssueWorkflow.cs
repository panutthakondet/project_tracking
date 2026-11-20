using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Services.Security;

public static class SecurityIssueWorkflow
{
    public static string? FromBaStatus(string status) => status switch { "PASS"=>"VerifiedFixed", "REJECT"=>"AcceptedRisk", "FAIL"=>"InProgress", _=>null };
    public static string? FromDevStatus(string status) => status=="FIXED"?"FixedPendingVerification":null;

    // Source links store finding IDs, so return the latest completed scan containing a linked finding.
    public static async Task<Dictionary<int,long>> ScanLinksAsync(AppDbContext db,int projectId,int[] issueIds)
    {
        var rows=await (from link in db.SecurityFindingIssueLinkRows.AsNoTracking()
            join finding in db.SecurityFindingRows on link.FindingId equals finding.Id
            join system in db.SecuritySystemRows on finding.SystemId equals system.Id
            join observation in db.SecurityScanFindingRows on finding.Id equals observation.FindingId
            join scan in db.SecurityScanRows on observation.ScanId equals scan.Id
            where issueIds.Contains(link.IssueId)&&system.ProjectId==projectId&&scan.Status=="Completed"
            orderby scan.CreatedAt descending,scan.Id descending
            select new{link.IssueId,ScanId=scan.Id}).ToListAsync();
        return rows.GroupBy(x=>x.IssueId).ToDictionary(g=>g.Key,g=>g.First().ScanId);
    }

    // Stage with the Issue change; the caller saves both in the same SaveChanges transaction.
    public static async Task StageStatusAsync(AppDbContext db,ProjectIssue issue,string status,int userId)
    {
        if(!SecurityValues.ReviewStatuses.Contains(status))throw new ArgumentOutOfRangeException(nameof(status));
        var findings=await (from finding in db.SecurityFindingRows
            join system in db.SecuritySystemRows on finding.SystemId equals system.Id
            where system.ProjectId==issue.ProjectId&&db.SecurityFindingIssueLinkRows.Any(l=>l.FindingId==finding.Id&&l.IssueId==issue.IssueId)
            select finding).ToListAsync();
        foreach(var finding in findings){
            if(finding.ReviewStatus==status)continue;
            finding.ReviewStatus=status;
            db.SecurityFindingHistoryRows.Add(new(){FindingId=finding.Id,Status=status,CreatedBy=userId,
                Note=$"อัปเดตจาก Issue #{issue.IssueId}: IssueStatus={issue.IssueStatus}, DevStatus={issue.DevStatus}"});
        }
    }
}
