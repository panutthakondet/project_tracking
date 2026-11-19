using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Services.Security;
namespace ProjectTracking.Controllers;
[AllowAnonymous,SecurityWorkerKey,Route("api/security-worker")]
public class SecurityWorkerController(SecurityScanService scans,AppDbContext db,SecurityStorage storage):Controller
{
    public record LeaseRequest(string LeaseToken,string? Error=null,ScanProgressUpdate? Progress=null);
    [HttpPost("claim")]
    public async Task<IActionResult> Claim(CancellationToken ct){var job=await scans.ClaimAsync(ct);return job==null?NoContent():Json(job);}
    [HttpPost("heartbeat/{id:long}")]
    public async Task<IActionResult> Heartbeat(long id,[FromBody]LeaseRequest request,CancellationToken ct)
    {
        if(request.Progress!=null&&!SecurityProgress.IsValid(request.Progress))return BadRequest();
        return await scans.HeartbeatAsync(id,request.LeaseToken,ct,request.Progress)?NoContent():Conflict();
    }
    [HttpGet("artifact/{id:long}")]
    public async Task<IActionResult> Artifact(long id,CancellationToken ct)
    {
        if(!await scans.HasLeaseAsync(id,Request.Headers["X-Scan-Lease"].ToString(),ct))return NotFound();
        var versionId=await db.SecurityScanRows.Where(x=>x.Id==id).Select(x=>x.VersionId).SingleAsync(ct);
        var file=await db.SecurityArtifactRows.AsNoTracking().SingleOrDefaultAsync(x=>x.VersionId==versionId,ct);if(file==null)return NotFound();
        return PhysicalFile(storage.PathFor(file.StorageKey),"application/octet-stream",file.OriginalName);
    }
    [HttpPost("complete/{id:long}"),RequestSizeLimit(54525952),RequestFormLimits(MultipartBodyLengthLimit=54525952)]
    public async Task<IActionResult> Complete(long id,string leaseToken,string engineVersion,string coverage,IFormFile report,CancellationToken ct)
    {
        if(report==null||report.Length==0||report.Length>50*1024*1024)return BadRequest();
        try{using var reader=new StreamReader(report.OpenReadStream());var json=await reader.ReadToEndAsync(ct);await scans.CompleteAsync(id,leaseToken,json,new(engineVersion,coverage),ct);return NoContent();}
        catch(Exception e)when(e is InvalidOperationException or System.Text.Json.JsonException){return BadRequest(new{error="รายงานไม่ถูกต้องหรืองานหมดสิทธิ์ส่งผล"});}
    }
    [HttpPost("fail/{id:long}")]
    public async Task<IActionResult> Fail(long id,[FromBody]LeaseRequest request,CancellationToken ct)
    {
        var count=await db.SecurityScanRows.Where(x=>x.Id==id&&x.Status=="Running"&&x.LeaseToken==request.LeaseToken&&x.LeaseUntil>DateTime.UtcNow).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Failed").SetProperty(x=>x.Error,SecurityValues.Cut(request.Error,2000)).SetProperty(x=>x.FinishedAt,DateTime.UtcNow),ct);
        return count==1?NoContent():Conflict();
    }
}
