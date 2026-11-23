using ProjectTracking.Models;
namespace ProjectTracking.Services.Security;

public static class SecurityScanComparison
{
    public static IQueryable<SecurityScan> Candidates(IQueryable<SecurityScan> scans,
        IQueryable<SecurityWebsiteScanPart> parts, long[] versionIds, SecurityScan current,
        string? section, string legacySection)
    {
        var query=scans.Where(x=>versionIds.Contains(x.VersionId)&&x.Id!=current.Id&&x.Status=="Completed"
            &&x.Engine==current.Engine&&x.Mode==current.Mode&&x.Workflow==current.Workflow
            &&x.TargetUrl==current.TargetUrl&&x.RequiresLogin==current.RequiresLogin);
        // Login saves use fresh immutable profile IDs; changing that ID must not hide old runs.
        // Keep Website sections isolated even when they share the same URL.
        if(section!=null)query=query.Where(x=>parts.Any(p=>p.ScanId==x.Id&&p.Section==section)
            ||(section==legacySection&&!parts.Any(p=>p.ScanId==x.Id)));
        return query.OrderByDescending(x=>x.CreatedAt).Take(50);
    }
}
