using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
namespace ProjectTracking.Services.Security;

public sealed record SecurityScanRun(SecurityScan Scan,IReadOnlyList<SecurityScan> Scans,IReadOnlyList<SecurityWebsiteScanPart> Parts)
{
    public bool IsWebsiteBatch=>Parts.Count>0;
    public long[] Ids=>Scans.Select(x=>x.Id).ToArray();
    public bool HasResults=>Scans.Any(x=>x.Status=="Completed");
    public bool Terminal=>Scans.All(x=>x.Status is "Completed" or "Failed" or "Cancelled");
    public string Section(long scanId)=>Parts.FirstOrDefault(x=>x.ScanId==scanId)?.Section??"";
    public static SecurityScanRun Create(IEnumerable<SecurityScan> source,IEnumerable<SecurityWebsiteScanPart> sourceParts)
    {
        var scans=source.OrderBy(x=>x.Id).ToList();
        var parts=sourceParts.Where(x=>scans.Any(s=>s.Id==x.ScanId)).ToList();
        var first=scans[0];
        var status=scans.Any(x=>x.Status is "Running" or "Processing")?"Running":scans.Any(x=>x.Status=="Queued")?"Queued":scans.All(x=>x.Status=="Completed")?"Completed":scans.Any(x=>x.Status=="Failed")?"Failed":"Cancelled";
        // Display model only: never change the persisted per-engine jobs or their leases.
        var display=new SecurityScan{Id=first.Id,VersionId=first.VersionId,Workflow=first.Workflow,Engine=first.Engine,Mode=first.Mode,Status=status,
            CreatedAt=scans.Min(x=>x.CreatedAt),StartedAt=scans.Min(x=>x.StartedAt),
            FinishedAt=scans.All(x=>x.Status is "Completed" or "Failed" or "Cancelled")?scans.Max(x=>x.FinishedAt):null,
            TargetUrl=parts.Count>0?string.Join(" · ",scans.Select(x=>x.TargetUrl).Distinct()):first.TargetUrl,RequiresLogin=first.RequiresLogin,AuthProfile=first.AuthProfile,EngineVersion=first.EngineVersion,
            Coverage=string.Join(" · ",scans.Select(x=>parts.FirstOrDefault(p=>p.ScanId==x.Id) is {} part?SecurityValues.SectionLabel(part.Section)+": "+SecurityValues.StatusLabel(x.Status)+" — "+x.Coverage:x.Coverage)),
            Error=string.Join("\n",scans.Where(x=>x.Error!=null).Select(x=>(parts.FirstOrDefault(p=>p.ScanId==x.Id) is {} p?SecurityValues.SectionLabel(p.Section)+": ":"")+x.Error))};
        if(string.IsNullOrEmpty(display.Error))display.Error=null;
        return new(display,scans,parts);
    }
    public static List<SecurityScanRun> Group(IEnumerable<SecurityScan> scans,IEnumerable<SecurityWebsiteScanPart> parts)
    {
        var byId=parts.ToDictionary(x=>x.ScanId);
        return scans.GroupBy(s=>byId.TryGetValue(s.Id,out var p)?$"{s.VersionId}:{s.Workflow}:{s.Engine}:{s.Mode}:{p.BatchKey}":$"single:{s.Id}")
            .Select(g=>Create(g,g.Where(s=>byId.ContainsKey(s.Id)).Select(s=>byId[s.Id])))
            .OrderByDescending(x=>x.Scan.CreatedAt).ToList();
    }
    public static async Task<SecurityScanRun> LoadAsync(AppDbContext db,SecurityScan scan,CancellationToken ct)
    {
        var part=await db.SecurityWebsiteScanParts.AsNoTracking().SingleOrDefaultAsync(x=>x.ScanId==scan.Id,ct);
        if(part==null)return Create(new[]{scan},Array.Empty<SecurityWebsiteScanPart>());
        var siblings=await (from p in db.SecurityWebsiteScanParts join s in db.SecurityScanRows on p.ScanId equals s.Id
            where p.BatchKey==part.BatchKey&&s.VersionId==scan.VersionId&&s.Workflow==scan.Workflow&&s.Engine==scan.Engine&&s.Mode==scan.Mode select s).AsNoTracking().ToListAsync(ct);
        var ids=siblings.Select(x=>x.Id).ToArray();
        return Create(siblings,await db.SecurityWebsiteScanParts.AsNoTracking().Where(x=>ids.Contains(x.ScanId)).ToListAsync(ct));
    }
    public string ComparisonKey()=>string.Join("|",Scans.Select(s=>$"{Section(s.Id)}:{s.TargetUrl}:{s.RequiresLogin}").OrderBy(x=>x,StringComparer.Ordinal));
}
