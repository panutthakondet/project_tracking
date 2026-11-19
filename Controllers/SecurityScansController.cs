using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;
using ProjectTracking.ViewModels.Security;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
namespace ProjectTracking.Controllers;
[RequireMenu("SecurityScans.Index")]
public class SecurityScansController(AppDbContext db,SecurityStorage storage,SecurityScanService scans,IWebHostEnvironment env,SecurityLoginStore loginStore):BaseController
{
    private int UserId=>HttpContext.Session.GetInt32("UserId")!.Value;
    private bool Can(string key)=>string.Equals(HttpContext.Session.GetString("Role"),"ADMIN",StringComparison.OrdinalIgnoreCase)||(HttpContext.Session.GetString("Menus")??"").Split(',').Any(x=>x.Trim().Equals(key,StringComparison.OrdinalIgnoreCase));
    private async Task<int?> EmployeeId(CancellationToken ct)=>await db.LoginUsers.Where(u=>u.UserId==UserId).Select(u=>u.EmpId).FirstOrDefaultAsync(ct)??await db.Employees.Where(e=>e.LoginUserId==UserId).Select(e=>(int?)e.EmpId).FirstOrDefaultAsync(ct);
    private async Task<Project?> AllowedProject(int id,CancellationToken ct)
    {
        var project=await db.Projects.Include(p=>p.Coop).Include(p=>p.TeamMembers).AsNoTracking().SingleOrDefaultAsync(p=>p.ProjectId==id,ct);
        if(project==null)return null;
        if(string.Equals(HttpContext.Session.GetString("Role"),"ADMIN",StringComparison.OrdinalIgnoreCase)||Can("SecurityScans.ViewAllProjects"))return project;
        var employee=await EmployeeId(ct);
        return employee.HasValue&&(project.BaEmpId==employee||project.PmEmpId==employee||project.TeamMembers.Any(m=>m.EmpId==employee))?project:null;
    }
    private async Task<(SecurityScan Scan,SecurityVersion Version,SecuritySystem System,Project Project)?> Scope(long id,CancellationToken ct)
    {
        var scan=await db.SecurityScanRows.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(scan==null)return null;
        var version=await db.SecurityVersionRows.AsNoTracking().SingleAsync(x=>x.Id==scan.VersionId,ct);var system=await db.SecuritySystemRows.AsNoTracking().SingleAsync(x=>x.Id==version.SystemId,ct);var project=await AllowedProject(system.ProjectId,ct);
        return project==null?null:(scan,version,system,project);
    }
    public async Task<IActionResult> Index(int projectId,CancellationToken ct)
    {
        var project=await AllowedProject(projectId,ct);if(project==null)return NotFound();
        var systems=await db.SecuritySystemRows.AsNoTracking().Where(x=>x.ProjectId==projectId).OrderBy(x=>x.Name).ToListAsync(ct);var ids=systems.Select(x=>x.Id).ToArray();var versions=await db.SecurityVersionRows.AsNoTracking().Where(x=>ids.Contains(x.SystemId)).OrderByDescending(x=>x.CreatedAt).ToListAsync(ct);var vids=versions.Select(x=>x.Id).ToArray();
        return View(new SecurityProjectPage{CanApproveHosts=Can("SecurityScans.ApproveHosts"),HostApprovals=await db.SecurityHostApprovals.AsNoTracking().Where(x=>x.ProjectId==projectId).ToListAsync(ct),Project=project,Systems=systems,Versions=versions,Artifacts=await db.SecurityArtifactRows.AsNoTracking().Where(x=>vids.Contains(x.VersionId)).ToListAsync(ct),Scans=await db.SecurityScanRows.AsNoTracking().Where(x=>vids.Contains(x.VersionId)).OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync(ct),CanManage=Can("SecurityScans.Edit"),WorkerConfigured=scans.WorkerConfigured});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> SaveLogin(long systemId,bool enabled,string? loginUrl,string? username,string? password,string? loggedInRegex,string? requestData,CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        var system=await db.SecuritySystemRows.SingleOrDefaultAsync(x=>x.Id==systemId,ct);
        if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(SecurityValues.IsMobile(system.Kind))return BadRequest();
        string? profile=null;
        try{
            if(enabled){
                profile=await loginStore.SaveAsync(system.Id,new SecurityLoginCredentials{
                    LoginUrl=loginUrl?.Trim()??"",Username=username??"",Password=password??"",LoggedInRegex=loggedInRegex??"",
                    RequestData=string.IsNullOrWhiteSpace(requestData)?"username={%username%}&password={%password%}":requestData
                },system.TargetUrl,ct);
            }
            system.RequiresLogin=enabled;system.AuthProfile=profile;
            await db.SaveChangesAsync(ct);
            TempData["Success"]=enabled?"บันทึก Login แบบเข้ารหัสแล้ว ใช้กับรอบสแกนใหม่ได้ทันที":"ปิดการ Login สำหรับรอบสแกนใหม่แล้ว";
        }catch(InvalidOperationException error){
            if(profile!=null)loginStore.DeleteUncommitted(profile);
            TempData["Error"]=error.Message;
        }catch{
            if(profile!=null)loginStore.DeleteUncommitted(profile);
            throw;
        }
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.ApproveHosts")]
    public async Task<IActionResult> ApproveHost(long systemId,bool approved,CancellationToken ct)
    {
        var system=await db.SecuritySystemRows.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==systemId,ct);
        if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(SecurityValues.IsMobile(system.Kind))return BadRequest();
        try{
            await scans.SetHostApprovalAsync(system.ProjectId,SecurityScanService.TargetHost(system.TargetUrl),approved,UserId,ct);
            TempData["Success"]=approved?"อนุมัติโดเมนแล้ว สั่งสแกนได้ทันทีโดยไม่ต้อง Restart Worker":"ถอนการอนุมัติโดเมนแล้ว งานที่ยังไม่เริ่มจะไม่รับไปสแกน";
        }catch(InvalidOperationException error){TempData["Error"]=error.Message;}
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> AddSystem(int projectId,string name,string kind,string? targetUrl,bool requiresLogin,string? authProfile,CancellationToken ct)
    {
        if(await AllowedProject(projectId,ct)==null)return NotFound();
        try{
            if(string.IsNullOrWhiteSpace(name)||name.Length>160||!SecurityValues.Kinds.Contains(kind))throw new InvalidOperationException("กรอกชื่อระบบและประเภทให้ถูกต้อง");
            if(!SecurityValues.IsMobile(kind))SecurityScanService.ValidateUrl(targetUrl);
            if(authProfile?.Length>80||authProfile?.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_')==true)throw new InvalidOperationException("ชื่อโปรไฟล์ใช้ได้เฉพาะตัวอักษรอังกฤษ ตัวเลข - และ _");
            if(requiresLogin&&string.IsNullOrWhiteSpace(authProfile))throw new InvalidOperationException("กรุณาระบุชื่อโปรไฟล์ Login บนเครื่องสแกน");
            db.SecuritySystemRows.Add(new(){ProjectId=projectId,Name=name.Trim(),Kind=kind,TargetUrl=SecurityValues.IsMobile(kind)?null:targetUrl?.Trim(),RequiresLogin=requiresLogin,AuthProfile=authProfile,CreatedBy=UserId});await db.SaveChangesAsync(ct);TempData["Success"]="เพิ่มระบบแล้ว";
        }catch(InvalidOperationException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Index),new{projectId});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit"),RequestSizeLimit(268435456),RequestFormLimits(MultipartBodyLengthLimit=268435456)]
    public async Task<IActionResult> AddVersion(long systemId,string versionName,string? buildNumber,string? gitCommit,string environment,string? changeNotes,IFormFile? application,CancellationToken ct)
    {
        var system=await db.SecuritySystemRows.SingleOrDefaultAsync(x=>x.Id==systemId,ct);if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        string? key=null;var committed=false;
        try{
            if(string.IsNullOrWhiteSpace(versionName)||versionName.Length>100||buildNumber?.Length>100||gitCommit?.Length>64||changeNotes?.Length>4000||environment is not ("UAT" or "Production"))throw new InvalidOperationException("ข้อมูลเวอร์ชันไม่ถูกต้อง");
            var version=new SecurityVersion{SystemId=systemId,VersionName=versionName.Trim(),BuildNumber=buildNumber,GitCommit=gitCommit,Environment=environment,ChangeNotes=changeNotes,CreatedBy=UserId};
            SecurityArtifact? artifact=null;
            if(SecurityValues.IsMobile(system.Kind)){
                var ext=system.Kind=="Android"?".apk":".ipa";
                if(application==null||!Path.GetExtension(application.FileName).Equals(ext,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("กรุณาเลือกไฟล์ "+ext);
                await using var upload=application.OpenReadStream();var file=await storage.WriteAsync(upload,ext,250L*1024*1024,ct);key=file.Key;storage.ValidateApplication(key,system.Kind);
                artifact=new(){OriginalName=SecurityValues.Cut(Path.GetFileName(application.FileName),255),StorageKey=key,Sha256=file.Hash,Size=file.Size};
            }
            await using var tx=await db.Database.BeginTransactionAsync(ct);db.SecurityVersionRows.Add(version);await db.SaveChangesAsync(ct);
            if(artifact!=null){artifact.VersionId=version.Id;db.SecurityArtifactRows.Add(artifact);await db.SaveChangesAsync(ct);}await tx.CommitAsync(ct);committed=true;TempData["Success"]="เก็บเวอร์ชันใหม่แล้ว ไม่ทับเวอร์ชันเดิม";
        }catch(Exception e)when(e is InvalidOperationException or InvalidDataException){TempData["Error"]=e.Message;}
        finally{if(!committed&&key!=null)storage.Delete(key);}
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> Queue(long versionId,string mode,string requestKey,CancellationToken ct)
    {
        var version=await db.SecurityVersionRows.FindAsync([versionId],ct);if(version==null)return NotFound();var system=await db.SecuritySystemRows.FindAsync([version.SystemId],ct);if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        try{var id=await scans.QueueAsync(versionId,mode,requestKey,UserId,ct);return RedirectToAction(nameof(Scan),new{id});}catch(InvalidOperationException e){TempData["Error"]=e.Message;return RedirectToAction(nameof(Index),new{projectId=system.ProjectId});}
    }
    public async Task<IActionResult> Scan(long id,long? baselineId,CancellationToken ct,int page=1)
    {
        var scope=await Scope(id,ct);if(scope==null)return NotFound();var (scan,version,system,project)=scope.Value;
        var observationQuery=db.SecurityScanFindingRows.AsNoTracking().Where(x=>x.ScanId==id);
        var counts=await observationQuery.GroupBy(x=>x.Severity).Select(g=>new{Severity=g.Key,Count=g.Count()}).ToDictionaryAsync(x=>x.Severity,x=>x.Count,ct);
        var total=counts.Values.Sum();const int pageSize=50;
        var pageNumber=Math.Clamp(page,1,Math.Max(1,(int)Math.Ceiling(total/(double)pageSize)));
        var allFindingIds=await observationQuery.Select(x=>x.FindingId).ToListAsync(ct);
        var observations=await observationQuery.OrderByDescending(x=>x.Severity=="Critical"?5:x.Severity=="High"?4:x.Severity=="Medium"?3:x.Severity=="Low"?2:1).ThenBy(x=>x.Title).ThenBy(x=>x.Id).Skip((pageNumber-1)*pageSize).Take(pageSize).ToListAsync(ct);var fids=observations.Select(x=>x.FindingId).ToArray();var versions=await db.SecurityVersionRows.Where(x=>x.SystemId==system.Id&&x.Environment==version.Environment).Select(x=>x.Id).ToListAsync(ct);
        var baselines=await db.SecurityScanRows.AsNoTracking().Where(x=>versions.Contains(x.VersionId)&&x.Id!=id&&x.Status=="Completed"&&x.Engine==scan.Engine&&x.Mode==scan.Mode&&x.TargetUrl==scan.TargetUrl&&x.RequiresLogin==scan.RequiresLogin&&x.AuthProfile==scan.AuthProfile).OrderByDescending(x=>x.CreatedAt).Take(50).ToListAsync(ct);
        return View(new SecurityScanPage{TotalObservations=total,PageNumber=pageNumber,PageSize=pageSize,BaselineId=baselineId,SeverityCounts=counts,AllFindingIds=allFindingIds,Progress=await db.SecurityScanProgressRows.AsNoTracking().SingleOrDefaultAsync(x=>x.ScanId==id,ct),Project=project,System=system,Version=version,Scan=scan,Observations=observations,Findings=await db.SecurityFindingRows.AsNoTracking().Where(x=>fids.Contains(x.Id)).ToListAsync(ct),History=await db.SecurityFindingHistoryRows.AsNoTracking().Where(x=>fids.Contains(x.FindingId)).OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync(ct),Links=await db.SecurityFindingIssueLinkRows.AsNoTracking().Where(x=>fids.Contains(x.FindingId)).ToListAsync(ct),Employees=await db.Employees.AsNoTracking().Where(x=>x.Status=="ACTIVE").OrderBy(x=>x.EmpName).ToListAsync(ct),ExistingIssues=await db.ProjectIssues.AsNoTracking().Where(x=>x.ProjectId==project.ProjectId).OrderByDescending(x=>x.IssueId).Take(200).ToListAsync(ct),Baselines=baselines,BaselineFindings=baselineId.HasValue&&baselines.Any(x=>x.Id==baselineId)?await db.SecurityScanFindingRows.AsNoTracking().Where(x=>x.ScanId==baselineId).Select(x=>new SecurityScanFinding{FindingId=x.FindingId}).ToListAsync(ct):null,CanManage=Can("SecurityScans.Edit"),CanCreateIssue=Can("SecurityScans.CreateIssues")&&Can("ProjectIssues.Create")});
    }
    [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Progress(long id,CancellationToken ct)
    {
        var scope=await Scope(id,ct);if(scope==null)return NotFound();
        var progress=await db.SecurityScanProgressRows.AsNoTracking().SingleOrDefaultAsync(x=>x.ScanId==id,ct);
        return Json(ScanProgressPage.From(scope.Value.Scan,progress));
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> Cancel(long id,CancellationToken ct)
    {
        if(await Scope(id,ct)==null)return NotFound();await db.SecurityScanRows.Where(x=>x.Id==id&&(x.Status=="Queued"||x.Status=="Running")).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Cancelled").SetProperty(x=>x.FinishedAt,DateTime.UtcNow),ct);return RedirectToAction(nameof(Scan),new{id});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.CreateIssues"),RequireMenu("ProjectIssues.Create")]
    public async Task<IActionResult> CreateIssues(long scanId,long[] observationIds,int employeeId,DateTime startDate,DateTime endDate,CancellationToken ct)
    {
        if(await Scope(scanId,ct)==null)return NotFound();
        var ids=observationIds.Distinct().Take(51).ToArray();if(ids.Length==0||ids.Length>50||await db.SecurityScanFindingRows.CountAsync(x=>x.ScanId==scanId&&ids.Contains(x.Id),ct)!=ids.Length)return BadRequest();
        try{if(startDate==default||endDate==default)throw new InvalidOperationException("กรุณาระบุวันเริ่มและกำหนดส่ง");var creator=await EmployeeId(ct);foreach(var id in ids)await scans.CreateIssueAsync(id,employeeId,startDate,endDate,UserId,creator,ct);TempData["Success"]="สร้างหรือเปิดการเชื่อม Issue แล้ว";}catch(InvalidOperationException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Scan),new{id=scanId});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> Review(long observationId,string status,string note,int? assignedEmpId,DateTime? dueDate,CancellationToken ct)
    {
        var observation=await db.SecurityScanFindingRows.FindAsync([observationId],ct);if(observation==null||await Scope(observation.ScanId,ct)==null)return NotFound();
        if(!SecurityValues.ReviewStatuses.Contains(status)||string.IsNullOrWhiteSpace(note)||note.Length>4000)return BadRequest();
        if(assignedEmpId.HasValue&&!await db.Employees.AnyAsync(x=>x.EmpId==assignedEmpId&&x.Status=="ACTIVE",ct))return BadRequest();
        var finding=await db.SecurityFindingRows.SingleAsync(x=>x.Id==observation.FindingId,ct);finding.ReviewStatus=status;finding.AssignedEmpId=assignedEmpId;finding.DueDate=dueDate;
        db.SecurityFindingHistoryRows.Add(new(){FindingId=finding.Id,Status=status,Note=note,CreatedBy=UserId});await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Scan),new{id=observation.ScanId});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.CreateIssues"),RequireMenu("ProjectIssues.Create")]
    public async Task<IActionResult> LinkIssue(long observationId,int issueId,CancellationToken ct)
    {
        var observation=await db.SecurityScanFindingRows.FindAsync([observationId],ct);if(observation==null)return NotFound();var scope=await Scope(observation.ScanId,ct);if(scope==null)return NotFound();
        if(!await db.ProjectIssues.AnyAsync(x=>x.IssueId==issueId&&x.ProjectId==scope.Value.Project.ProjectId,ct))return BadRequest();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.SecurityFindingRows.Where(x=>x.Id==observation.FindingId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastSeenAt,x=>x.LastSeenAt),ct);
        if(!await db.SecurityFindingIssueLinkRows.AnyAsync(x=>x.FindingId==observation.FindingId,ct)){db.SecurityFindingIssueLinkRows.Add(new(){FindingId=observation.FindingId,IssueId=issueId,CreatedBy=UserId});await db.SaveChangesAsync(ct);}
        await tx.CommitAsync(ct);
        return RedirectToAction(nameof(Scan),new{id=observation.ScanId});
    }
    public async Task<IActionResult> Report(long id,string format="json",CancellationToken ct=default)
    {
        var scope=await Scope(id,ct);if(scope==null||scope.Value.Scan.Status!="Completed")return NotFound();
        if(format=="json"){var report=await db.SecurityReportRows.AsNoTracking().SingleOrDefaultAsync(x=>x.ScanId==id&&x.Format=="json",ct);if(report==null)return NotFound();return PhysicalFile(storage.PathFor(report.StorageKey),"application/json",$"security-scan-{id}.json");}
        if(format is not ("pdf" or "md"))return BadRequest();
        var items=await db.SecurityScanFindingRows.AsNoTracking().Where(x=>x.ScanId==id).ToListAsync(ct);
        if(format=="md")return File(System.Text.Encoding.UTF8.GetBytes(SecurityMarkdownReport.Create(scope.Value.Scan,scope.Value.Version,scope.Value.System,scope.Value.Project.ProjectDisplayName,items)),"text/markdown; charset=utf-8",$"security-scan-{id}.md");
        var font=Path.Combine(env.WebRootPath,"fonts","THSarabunNew.ttf");if(System.IO.File.Exists(font)){using var stream=System.IO.File.OpenRead(font);QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("SecurityThai",stream);}
        var pdf=QuestPDF.Fluent.Document.Create(container=>container.Page(page=>{page.Size(PageSizes.A4);page.Margin(28);page.DefaultTextStyle(x=>x.FontSize(12).FontFamily(System.IO.File.Exists(font)?"SecurityThai":"Lato"));page.Content().Column(column=>{column.Item().Text($"Security Scan #{id}").FontSize(22).Bold();column.Item().Text(scope.Value.Project.ProjectDisplayName);column.Item().Text($"{scope.Value.System.Name} / {scope.Value.Version.VersionName} / {scope.Value.Version.Environment}\n{scope.Value.Scan.Engine} {scope.Value.Scan.EngineVersion}\n{scope.Value.Scan.Coverage}\nUTC: {scope.Value.Scan.FinishedAt:yyyy-MM-dd HH:mm}");foreach(var item in items.OrderByDescending(x=>SecurityValues.Rank(x.Severity))){column.Item().PaddingTop(16).Text($"[{item.Severity}] {item.Title}").Bold();column.Item().Text($"{item.Location}\n{item.Description}\nEvidence: {item.Evidence}\nRemediation: {item.Remediation}\nReferences: {item.References}");}});page.Footer().AlignRight().Text(t=>{t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});})).GeneratePdf();
        return File(pdf,"application/pdf",$"security-scan-{id}.pdf");
    }
}
