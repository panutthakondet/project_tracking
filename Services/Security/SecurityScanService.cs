using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using System.Text;
namespace ProjectTracking.Services.Security;
public class SecurityScanService(AppDbContext db,SecurityStorage storage,IConfiguration config)
{
    public bool WorkerConfigured=>(Environment.GetEnvironmentVariable("SECURITY_WORKER_KEY")??config["SECURITY_WORKER_KEY"])?.Length>=32;
    public static Uri ValidateUrl(string? value)
    {
        if(value?.Length>2000||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo!=""||uri.Fragment!=""||uri.HostNameType==UriHostNameType.Unknown)throw new InvalidOperationException("ระบุ URL http/https โดยไม่ใส่รหัสผ่านหรือ fragment ใน URL");
        return uri;
    }
    public static string TargetHost(string? url)
    {
        var host=ValidateUrl(url).IdnHost.TrimEnd('.').ToLowerInvariant();
        if(host.Length is 0 or >253)throw new InvalidOperationException("ชื่อโดเมนไม่ถูกต้อง");
        return host;
    }
    public Task<bool> IsHostApprovedAsync(int projectId,string host,CancellationToken ct)=>db.SecurityHostApprovals.AnyAsync(x=>x.ProjectId==projectId&&x.Host==host&&x.IsApproved,ct);
    public async Task SetHostApprovalAsync(int projectId,string host,bool approved,int userId,CancellationToken ct)
    {
        var row=await db.SecurityHostApprovals.SingleOrDefaultAsync(x=>x.ProjectId==projectId&&x.Host==host,ct);
        if(row==null){row=new(){ProjectId=projectId,Host=host};db.SecurityHostApprovals.Add(row);}
        row.IsApproved=approved;row.UpdatedBy=userId;row.UpdatedAt=DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task<long> QueueAsync(long versionId,string mode,string requestKey,int userId,CancellationToken ct)
    {
        if(!WorkerConfigured)throw new InvalidOperationException("ยังไม่ได้ตั้งค่า SECURITY_WORKER_KEY และเครื่องสแกน กรุณาติดตั้งตามคู่มือก่อน");
        if(!Guid.TryParse(requestKey,out _))throw new InvalidOperationException("รหัสคำขอไม่ถูกต้อง");
        var existing=await db.SecurityScanRows.SingleOrDefaultAsync(x=>x.RequestKey==requestKey,ct);if(existing!=null){if(existing.VersionId!=versionId||existing.CreatedBy!=userId)throw new InvalidOperationException("คำขอสแกนไม่ตรงกัน");return existing.Id;}
        var version=await db.SecurityVersionRows.SingleAsync(x=>x.Id==versionId,ct);var system=await db.SecuritySystemRows.SingleAsync(x=>x.Id==version.SystemId,ct);
        if(SecurityValues.IsMobile(system.Kind)){
            if(mode!="Static")throw new InvalidOperationException("แอปมือถือรองรับ Static Analysis ในรุ่นนี้");
            if(!await db.SecurityArtifactRows.AnyAsync(x=>x.VersionId==versionId,ct))throw new InvalidOperationException("เวอร์ชันนี้ยังไม่มีไฟล์ APK/IPA");
        }else{
            if(!await IsHostApprovedAsync(system.ProjectId,TargetHost(system.TargetUrl),ct))throw new InvalidOperationException("โดเมนนี้ยังไม่ได้รับอนุมัติ กรุณาให้ผู้มีสิทธิ์อนุมัติโดเมนจากหน้านี้ก่อนสั่งสแกน");
            ValidateUrl(system.TargetUrl);if(mode is not ("Passive" or "Active"))throw new InvalidOperationException("โหมดสแกนไม่ถูกต้อง");
            if(mode=="Active"&&version.Environment!="UAT")throw new InvalidOperationException("Active Scan เปิดใช้เฉพาะ UAT");
            if(system.RequiresLogin&&string.IsNullOrWhiteSpace(system.AuthProfile))throw new InvalidOperationException("กรุณาระบุโปรไฟล์ Login ที่ตั้งค่าบนเครื่องสแกน");
        }
        var scan=new SecurityScan{VersionId=versionId,Mode=mode,Engine=SecurityValues.IsMobile(system.Kind)?"MobSF":"ZAP",TargetUrl=system.TargetUrl,AuthProfile=system.AuthProfile,RequiresLogin=system.RequiresLogin,CreatedBy=userId,RequestKey=requestKey};
        db.SecurityScanRows.Add(scan);
        try{await db.SaveChangesAsync(ct);return scan.Id;}
        catch(DbUpdateException){
            db.Entry(scan).State=EntityState.Detached;
            var duplicate=await db.SecurityScanRows.AsNoTracking().SingleOrDefaultAsync(x=>x.RequestKey==requestKey,ct);
            if(duplicate!=null&&duplicate.VersionId==versionId&&duplicate.CreatedBy==userId)return duplicate.Id;
            throw;
        }
    }
    public async Task<SecurityJob?> ClaimAsync(CancellationToken ct)
    {
        var now=DateTime.UtcNow;
        await db.SecurityScanRows.Where(x=>x.Status=="Running"&&x.LeaseUntil<now).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Failed").SetProperty(x=>x.Error,"เครื่องสแกนขาดการติดต่อ กรุณาตรวจเครื่องแล้วสั่งรอบใหม่").SetProperty(x=>x.FinishedAt,now),ct);
        foreach(var id in await db.SecurityScanRows.AsNoTracking().Where(x=>x.Status=="Queued").OrderBy(x=>x.CreatedAt).Select(x=>x.Id).Take(10).ToListAsync(ct)){
            var token=Guid.NewGuid().ToString("N");
            var changed=await db.SecurityScanRows.Where(x=>x.Id==id&&x.Status=="Queued").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Running").SetProperty(x=>x.LeaseToken,token).SetProperty(x=>x.LeaseUntil,now.AddMinutes(3)).SetProperty(x=>x.StartedAt,now),ct);
            if(changed==0)continue;
            var scan=await db.SecurityScanRows.AsNoTracking().SingleAsync(x=>x.Id==id,ct);var version=await db.SecurityVersionRows.AsNoTracking().SingleAsync(x=>x.Id==scan.VersionId,ct);var system=await db.SecuritySystemRows.AsNoTracking().SingleAsync(x=>x.Id==version.SystemId,ct);var artifact=await db.SecurityArtifactRows.AsNoTracking().SingleOrDefaultAsync(x=>x.VersionId==version.Id,ct);
            string[] approvedHosts=[];
            if(scan.Engine=="ZAP"){
                var host=TargetHost(scan.TargetUrl);
                if(!await IsHostApprovedAsync(system.ProjectId,host,ct)){
                    await db.SecurityScanRows.Where(x=>x.Id==id&&x.LeaseToken==token&&x.Status=="Running").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Failed").SetProperty(x=>x.Error,"โดเมนถูกถอนหรือยังไม่ได้รับอนุมัติก่อนเริ่มงาน กรุณาตรวจสิทธิ์โดเมนแล้วสั่งรอบใหม่").SetProperty(x=>x.FinishedAt,DateTime.UtcNow),ct);
                    continue;
                }
                approvedHosts=[host];
            }
            return new(id,token,system.Kind,scan.Engine,scan.Mode,version.VersionName,version.BuildNumber,version.Environment,scan.TargetUrl,scan.RequiresLogin,scan.AuthProfile,artifact?.OriginalName,artifact?.Sha256,approvedHosts);
        }
        return null;
    }
    public async Task<bool> HeartbeatAsync(long id,string lease,CancellationToken ct,ScanProgressUpdate? progress=null)
    {
        if(progress!=null&&!SecurityProgress.IsValid(progress))throw new InvalidOperationException("Invalid scan progress");
        // Old workers keep the existing lease-only behaviour and do not require a progress row.
        if(progress==null)return await RenewLease(id,lease,ct)>0;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        if(await RenewLease(id,lease,ct)==0){await tx.RollbackAsync(ct);return false;}
        var scan=await db.SecurityScanRows.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
        var plan=SecurityProgress.Stages(scan.Engine,scan.Mode,scan.RequiresLogin);
        var nextIndex=Array.FindIndex(plan,s=>s.Stage==progress.Stage);
        if(nextIndex<0)throw new InvalidOperationException("Progress stage does not match the scan engine");
        var row=await db.SecurityScanProgressRows.SingleOrDefaultAsync(x=>x.ScanId==id,ct);
        if(row==null){row=new(){ScanId=id};db.SecurityScanProgressRows.Add(row);}
        var currentIndex=Array.FindIndex(plan,s=>s.Stage==row.Stage);
        if(nextIndex>=currentIndex){
            row.Stage=progress.Stage;row.Percent=progress.Percent;row.BytesDone=progress.BytesDone;row.BytesTotal=progress.BytesTotal;
        }
        row.UpdatedAt=DateTime.UtcNow;
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return true;
    }
    private Task<int> RenewLease(long id,string lease,CancellationToken ct)=>db.SecurityScanRows.Where(x=>x.Id==id&&x.Status=="Running"&&x.LeaseToken==lease&&x.LeaseUntil>DateTime.UtcNow).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LeaseUntil,DateTime.UtcNow.AddMinutes(3)),ct);
    public async Task<bool> HasLeaseAsync(long id,string lease,CancellationToken ct)=>await db.SecurityScanRows.AnyAsync(x=>x.Id==id&&x.Status=="Running"&&x.LeaseToken==lease&&x.LeaseUntil>DateTime.UtcNow,ct);
    public async Task CompleteAsync(long id,string lease,string json,ScanCompletion completion,CancellationToken ct)
    {
        var scan=await db.SecurityScanRows.SingleAsync(x=>x.Id==id,ct);
        await db.Entry(scan).ReloadAsync(ct);
        if(scan.Status=="Completed"&&scan.LeaseToken==lease)return; // Idempotent retry after a lost response.
        if(!await HasLeaseAsync(id,lease,ct))throw new InvalidOperationException("งานหมดสิทธิ์การส่งผลหรือถูกยกเลิก");
        var observations=SecurityReportParser.Parse(scan.Engine,json);var version=await db.SecurityVersionRows.SingleAsync(x=>x.Id==scan.VersionId,ct);
        var file=await storage.WriteAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)),".json",50*1024*1024,ct);
        try{
            await using var transaction=await db.Database.BeginTransactionAsync(ct);
            var claimed=await db.SecurityScanRows.Where(x=>x.Id==id&&x.Status=="Running"&&x.LeaseToken==lease&&x.LeaseUntil>DateTime.UtcNow).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Processing"),ct);
            if(claimed!=1)throw new InvalidOperationException("งานถูกยกเลิกหรือมีผู้ส่งผลแล้ว");
            var known=await db.SecurityFindingRows.Where(x=>x.SystemId==version.SystemId).ToDictionaryAsync(x=>x.Fingerprint,ct);
            foreach(var o in observations){
                var fingerprint=SecurityReportParser.Fingerprint(scan.Engine+":"+version.Environment,o);
                if(!known.TryGetValue(fingerprint,out var finding)){finding=new SecurityFinding{SystemId=version.SystemId,Fingerprint=fingerprint,Title=o.Title};db.SecurityFindingRows.Add(finding);known.Add(fingerprint,finding);}
            }
            await db.SaveChangesAsync(ct);
            foreach(var o in observations){
                var finding=known[SecurityReportParser.Fingerprint(scan.Engine+":"+version.Environment,o)];
                finding.LastSeenAt=DateTime.UtcNow;
                db.SecurityScanFindingRows.Add(new(){ScanId=id,FindingId=finding.Id,Title=o.Title,RuleId=o.RuleId,Severity=o.Severity,Location=o.Location,Description=o.Description,Evidence=o.Evidence,Remediation=o.Remediation,References=o.References,Cwe=o.Cwe});
            }
            db.SecurityReportRows.Add(new(){ScanId=id,StorageKey=file.Key,Sha256=file.Hash,Size=file.Size});
            scan.Status="Completed";scan.EngineVersion=SecurityValues.Cut(completion.EngineVersion,200);scan.Coverage=SecurityValues.Cut(completion.Coverage,2000);scan.FinishedAt=DateTime.UtcNow;
            // ExecuteUpdate bypasses tracking; explicitly persist the completed state.
            db.Entry(scan).Property(x=>x.Status).IsModified=true;
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
        }catch{storage.Delete(file.Key);throw;}
    }
    public async Task<int> CreateIssueAsync(long observationId,int employeeId,DateTime start,DateTime end,int userId,int? creatorEmpId,CancellationToken ct)
    {
        if(end.Date<start.Date)throw new InvalidOperationException("กำหนดส่งต้องไม่ก่อนวันเริ่ม");
        if(!await db.Employees.AnyAsync(e=>e.EmpId==employeeId&&e.Status=="ACTIVE",ct))throw new InvalidOperationException("เลือกผู้รับผิดชอบที่ยังใช้งาน");
        var observation=await db.SecurityScanFindingRows.SingleAsync(x=>x.Id==observationId,ct);var finding=await db.SecurityFindingRows.SingleAsync(x=>x.Id==observation.FindingId,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Atomic guard serializes duplicate Issue creation for this finding.
        await db.SecurityFindingRows.Where(x=>x.Id==finding.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastSeenAt,x=>x.LastSeenAt),ct);
        var old=await db.SecurityFindingIssueLinkRows.SingleOrDefaultAsync(x=>x.FindingId==finding.Id,ct);if(old!=null){await tx.CommitAsync(ct);return old.IssueId;}
        var system=await db.SecuritySystemRows.SingleAsync(x=>x.Id==finding.SystemId,ct);var scan=await db.SecurityScanRows.SingleAsync(x=>x.Id==observation.ScanId,ct);var version=await db.SecurityVersionRows.SingleAsync(x=>x.Id==scan.VersionId,ct);
        var details=$"Security scan #{scan.Id} / {scan.Engine}\nระบบ: {system.Name} / {version.Environment}\nVersion: {version.VersionName} Build: {version.BuildNumber}\nความเสี่ยง: {observation.Severity} / CWE: {observation.Cwe}\nตำแหน่ง: {observation.Location}\n\nรายละเอียด:\n{observation.Description}\n\nหลักฐาน:\n{observation.Evidence}\n\nแนวทางแก้ไขจากเครื่องมือ:\n{observation.Remediation}\n\nอ้างอิง:\n{observation.References}\n\nผลสแกน: /SecurityScans/Scan/{scan.Id}";
        var issue=new ProjectIssue{ProjectId=system.ProjectId,IssueName=SecurityValues.Cut("[Security / "+observation.Severity+"] "+observation.Title,500),IssueDetail=details,AssignTo=employeeId,CreatedBy=creatorEmpId,StartDate=start.Date,EndDate=end.Date,IssueStatus="OPEN",DevStatus="WIP",IssuePriority=observation.Severity is "Critical" or "High"?"HIGH":"NORMAL"};
        db.ProjectIssues.Add(issue);await db.SaveChangesAsync(ct);
        db.ProjectIssueStatusHistories.Add(new(){IssueId=issue.IssueId,NewStatus="OPEN",ChangedByEmpId=creatorEmpId??employeeId,ChangedAt=issue.CreatedAt});
        db.SecurityFindingIssueLinkRows.Add(new(){FindingId=finding.Id,IssueId=issue.IssueId,CreatedBy=userId});finding.AssignedEmpId=employeeId;finding.DueDate=end.Date;finding.ReviewStatus="InProgress";
        db.SecurityFindingHistoryRows.Add(new(){FindingId=finding.Id,Status="InProgress",Note="สร้าง Issue #"+issue.IssueId,CreatedBy=userId});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return issue.IssueId;
    }
}
