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
public class SecurityScansController(AppDbContext db,SecurityStorage storage,SecurityScanService scans,IWebHostEnvironment env,SecurityLoginStore loginStore,ILogger<SecurityScansController>? logger=null):BaseController
{
    [HttpGet]
    public async Task<IActionResult> LoginGuide(long systemId,string section,CancellationToken ct)
    {
        var system=await db.SecuritySystemRows.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==systemId,ct);
        if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(!SecurityValues.WebsiteSections.Contains(section))return BadRequest();
        Response.Headers.ContentDisposition="inline; filename=login-guide-"+section+".pdf";
        Response.Headers.CacheControl="private, no-store";
        return File(SecurityLoginGuide.Create(section,Path.Combine(env.WebRootPath,"fonts","THSarabunNew.ttf")),"application/pdf");
    }
    private string SelectedWorkflow=>Request.Query["workflow"]=="Development"?"Development":"Formal";
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
    [HttpGet]
    public async Task<IActionResult> ProjectReport(int projectId, CancellationToken ct)
    {
        var project = await AllowedProject(projectId, ct);
        if (project == null) return NotFound();

        var runs = await (from scan in db.SecurityScanRows.AsNoTracking()
                          join version in db.SecurityVersionRows on scan.VersionId equals version.Id
                          join system in db.SecuritySystemRows on version.SystemId equals system.Id
                          where system.ProjectId == projectId && scan.Workflow == "Formal"
                          select new { Scan = scan, system.Kind, version.SystemId, version.Environment }).ToListAsync(ct);
        var scanIds = runs.Select(row => row.Scan.Id).ToArray();
        var parts = await db.SecurityWebsiteScanParts.AsNoTracking()
            .Where(part => scanIds.Contains(part.ScanId)).ToListAsync(ct);
        var scopeByScan = runs.ToDictionary(row => row.Scan.Id, row => (row.SystemId, row.Environment));
        var comparisons = runs.GroupBy(row => SecurityValues.Category(row.Kind))
            .ToDictionary(group => group.Key, group =>
            {
                var ordered = SecurityScanRun.Group(group.Select(row => row.Scan), parts)
                    .OrderByDescending(run => run.Scan.CreatedAt)
                    .ThenByDescending(run => run.Scan.Id).ToList();
                var latest = ordered[0];
                var previous = latest.Scan.Status == "Completed" ? ordered.Skip(1).FirstOrDefault(run =>
                    run.Scan.Status == "Completed" && scopeByScan[run.Scan.Id] == scopeByScan[latest.Scan.Id] &&
                    run.Scan.Engine == latest.Scan.Engine && run.Scan.Mode == latest.Scan.Mode &&
                    run.ComparisonKey() == latest.ComparisonKey()) : null;
                return (Latest: latest, Previous: previous);
            });
        var completedIds = comparisons.Values.SelectMany(pair => pair.Latest.Scans
                .Concat(pair.Previous?.Scans ?? Array.Empty<SecurityScan>()))
            .Where(scan => scan.Status == "Completed").Select(scan => scan.Id).ToArray();

        var findings = await (from observation in db.SecurityScanFindingRows.AsNoTracking()
                              join finding in db.SecurityFindingRows on observation.FindingId equals finding.Id
                              where completedIds.Contains(observation.ScanId)
                              select new
                              {
                                  observation.ScanId, observation.FindingId, observation.Title,
                                  observation.Severity, finding.ReviewStatus,
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

        var groups = SecurityValues.Kinds.Select(category =>
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
                        previousKeys == null ? null : !previousKeys.Contains(group.Key));
                })
                .ToList();
            var currentKeys = findings.Where(item => ids.Contains(item.ScanId))
                .Select(item => (item.FindingId, Section: run!.Section(item.ScanId))).ToHashSet();
            return new ProjectSecurityReportGroup(category, run, items, previous?.Scan.Id,
                previousKeys?.Count(key => !currentKeys.Contains(key)) ?? 0);
        }).ToList();

        return View(new ProjectSecurityReport(projectId, project.ProjectDisplayName, groups));
    }

    public async Task<IActionResult> Index(int projectId,CancellationToken ct,string workflow="Formal")
    {
        if(workflow is not ("Formal" or "Development"))return BadRequest();
        var project=await AllowedProject(projectId,ct);if(project==null)return NotFound();
        var systems=await db.SecuritySystemRows.AsNoTracking().Where(x=>x.ProjectId==projectId&&x.Workflow==workflow).OrderBy(x=>x.Name).ToListAsync(ct);var ids=systems.Select(x=>x.Id).ToArray();var versions=await db.SecurityVersionRows.AsNoTracking().Where(x=>ids.Contains(x.SystemId)).OrderByDescending(x=>x.CreatedAt).ToListAsync(ct);var vids=versions.Select(x=>x.Id).ToArray();
        var history=await (from scan in db.SecurityScanRows join version in db.SecurityVersionRows on scan.VersionId equals version.Id join system in db.SecuritySystemRows on version.SystemId equals system.Id where system.ProjectId==projectId&&scan.Workflow==workflow orderby scan.CreatedAt descending select scan).AsNoTracking().Take(200).ToListAsync(ct);
        // Fill sibling jobs so the 200-row window never shows a partial Website run.
        var seedIds=history.Select(x=>x.Id).ToArray();
        var batchKeys=await db.SecurityWebsiteScanParts.Where(x=>seedIds.Contains(x.ScanId)).Select(x=>x.BatchKey).Distinct().ToListAsync(ct);
        var siblings=await (from part in db.SecurityWebsiteScanParts join scan in db.SecurityScanRows on part.ScanId equals scan.Id join version in db.SecurityVersionRows on scan.VersionId equals version.Id join system in db.SecuritySystemRows on version.SystemId equals system.Id where batchKeys.Contains(part.BatchKey)&&system.ProjectId==projectId&&scan.Workflow==workflow select scan).AsNoTracking().ToListAsync(ct);
        history=history.Concat(siblings).DistinctBy(x=>x.Id).OrderByDescending(x=>x.CreatedAt).ToList();
        var historyVersionIds=history.Select(x=>x.VersionId).Distinct().ToArray();
        var historyVersions=await db.SecurityVersionRows.AsNoTracking().Where(x=>historyVersionIds.Contains(x.Id)).ToListAsync(ct);
        var historySystemIds=historyVersions.Select(x=>x.SystemId).Distinct().ToArray();
        var historySystems=await db.SecuritySystemRows.AsNoTracking().Where(x=>historySystemIds.Contains(x.Id)).ToListAsync(ct);
        var page=new SecurityProjectPage{HistoryVersions=historyVersions,HistorySystems=historySystems,Workflow=workflow,CanDeleteScans=Can("SecurityScans.Delete"),CanApproveHosts=Can("SecurityScans.ApproveHosts"),HostApprovals=await db.SecurityHostApprovals.AsNoTracking().Where(x=>x.ProjectId==projectId).ToListAsync(ct),Project=project,Systems=systems,Versions=versions,Artifacts=await db.SecurityArtifactRows.AsNoTracking().Where(x=>vids.Contains(x.VersionId)).ToListAsync(ct),Scans=history,CanManage=Can("SecurityScans.Edit"),WorkerConfigured=scans.WorkerConfigured};
        page.IssueDefaults=await db.SecurityIssueDefaults.AsNoTracking().Where(x=>ids.Contains(x.SystemId)).ToListAsync(ct);
        page.CanAutoCreateIssues=Can("SecurityScans.CreateIssues")&&Can("ProjectIssues.Create");
        page.ResponsibleEmployees=await db.Employees.AsNoTracking().Where(x=>x.Status=="ACTIVE").OrderBy(x=>x.EmpName).ToListAsync(ct);
        page.WebsiteTargets=await db.SecurityWebsiteTargets.AsNoTracking().Where(x=>ids.Contains(x.SystemId)).ToListAsync(ct);
        var scanIds=page.Scans.Select(x=>x.Id).ToArray();
        page.WebsiteScanParts=await db.SecurityWebsiteScanParts.AsNoTracking().Where(x=>scanIds.Contains(x.ScanId)).ToListAsync(ct);
        // Legacy systems remain separate, preserving versions, reports and Issue links.
        foreach(var web in systems.Where(x=>!SecurityValues.IsMobile(x.Kind))){
            if(!page.WebsiteTargets.Any(x=>x.SystemId==web.Id)&&!string.IsNullOrWhiteSpace(web.TargetUrl))page.WebsiteTargets.Add(new(){SystemId=web.Id,Section=SecurityValues.LegacySection(web.Kind),TargetUrl=web.TargetUrl,Enabled=true,RequiresLogin=web.RequiresLogin,AuthProfile=web.AuthProfile});
        }
        if(page.CanManage){
            foreach(var target in page.WebsiteTargets.Where(x=>SecurityLoginStore.IsManaged(x.AuthProfile)&&x.Id>0)){
                try{var login=await loginStore.ReadAsync(target.SystemId,target.AuthProfile!,target.TargetUrl,ct,allowDraft:true);page.WebsiteLoginSettings[target.Id]=new(){HasPassword=!string.IsNullOrEmpty(login.Password),AuthenticationType=login.AuthenticationType,TokenPath=login.TokenPath,ScanStartUrl=login.ScanStartUrl,LoginUrl=login.LoginUrl,Username=login.Username,LoggedInRegex=login.LoggedInRegex,RequestData=login.RequestData};}
                catch(InvalidOperationException){page.UnreadableWebsiteLogins.Add(target.Id);}
            }
        }
        Response.Headers.CacheControl="no-store";
        if(page.CanManage){
            foreach(var system in systems.Where(x=>!SecurityValues.IsMobile(x.Kind)&&SecurityLoginStore.IsManaged(x.AuthProfile))){
                try{
                    var login=await loginStore.ReadAsync(system.Id,system.AuthProfile!,system.TargetUrl,ct);
                    page.LoginSettings[system.Id]=new SecurityLoginSettings{AuthenticationType=login.AuthenticationType,TokenPath=login.TokenPath,ScanStartUrl=login.ScanStartUrl,LoginUrl=login.LoginUrl,Username=login.Username,LoggedInRegex=login.LoggedInRegex,RequestData=login.RequestData};
                }catch(InvalidOperationException){page.UnreadableLoginSettings.Add(system.Id);}
            }
        }
        return View(page);
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> SaveWebsiteTarget(long systemId,string section,string? targetUrl,bool enabled,bool requiresLogin,SecurityLoginCredentials login,CancellationToken ct,string? targetPath=null,string? loginPath=null,string? scanStartPath=null)
    {
        Response.Headers.CacheControl="no-store";
        var system=await db.SecuritySystemRows.SingleOrDefaultAsync(x=>x.Id==systemId,ct);
        if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(system.Workflow!=SelectedWorkflow)return BadRequest("ระบบอยู่คนละรูปแบบการทำงาน");
        if(SecurityValues.IsMobile(system.Kind)||!SecurityValues.WebsiteSections.Contains(section))return BadRequest();
        if(section=="WebInfo")requiresLogin=false;
        login.AuthenticationType??="Form";login.TokenPath??="token";login.LoginUrl??="";login.Username??="";login.Password??="";login.LoggedInRegex??="";
        login.RequestData??=login.AuthenticationType=="JsonBearer"?"{\"username\":\"{%username%}\",\"password\":\"{%password%}\"}":"username={%username%}&password={%password%}";
        string? newProfile=null;
        try{
            var targets=await db.SecurityWebsiteTargets.Where(x=>x.SystemId==systemId).ToListAsync(ct);
            if(targets.Count==0&&!string.IsNullOrWhiteSpace(system.TargetUrl)){
                var legacy=new SecurityWebsiteTarget{SystemId=systemId,Section=SecurityValues.LegacySection(system.Kind),TargetUrl=system.TargetUrl,Enabled=true,RequiresLogin=system.RequiresLogin,AuthProfile=system.AuthProfile};
                targets.Add(legacy);db.SecurityWebsiteTargets.Add(legacy);
            }
            var target=targets.SingleOrDefault(x=>x.Section==section);
            if(target==null){target=new(){SystemId=systemId,Section=section};db.SecurityWebsiteTargets.Add(target);}
            var url=targetPath!=null?SecurityValues.WebsiteUrl(system.TargetUrl,targetPath.Trim()):targetUrl?.Trim()??"";
            if(loginPath!=null)login.LoginUrl=string.IsNullOrWhiteSpace(loginPath)?"":SecurityValues.WebsiteUrl(system.TargetUrl,loginPath.Trim());
            if(scanStartPath!=null)login.ScanStartUrl=string.IsNullOrWhiteSpace(scanStartPath)?null:SecurityValues.WebsiteUrl(system.TargetUrl,scanStartPath.Trim());
            if (!string.IsNullOrWhiteSpace(url)) SecurityScanService.ValidateUrl(url);
            if (section != "WebInfo")
            {
                newProfile = await loginStore.SaveAsync(systemId, login, url, ct, target.AuthProfile, allowDraft: true);
                target.AuthProfile = newProfile;
            }
            else target.AuthProfile = null;
            target.Enabled=enabled;
            target.TargetUrl=url;
            target.RequiresLogin=requiresLogin;
            await db.SaveChangesAsync(ct);
            TempData["Success"]=$"บันทึก {SecurityValues.SectionLabel(section)} แล้ว"+
                (enabled&&requiresLogin&&!SecurityLoginStore.IsManaged(target.AuthProfile)?" กรุณาตั้งค่า Login ก่อนนำเข้าคิวสแกน":"");
        }catch(InvalidOperationException error){if(newProfile!=null)loginStore.DeleteUncommitted(newProfile);TempData["Error"]=error.Message;}
        catch{if(newProfile!=null)loginStore.DeleteUncommitted(newProfile);throw;}
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("SecurityScans.Delete")]
    public async Task<IActionResult> DeleteWebsiteTarget(long systemId, string section, CancellationToken ct)
    {
        var system = await db.SecuritySystemRows.AsNoTracking().SingleOrDefaultAsync(x => x.Id == systemId, ct);
        if (system == null || await AllowedProject(system.ProjectId, ct) == null) return NotFound();
        if (system.Workflow != SelectedWorkflow || SecurityValues.IsMobile(system.Kind)
            || !SecurityValues.WebsiteSections.Contains(section)) return BadRequest();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.SecuritySystemRows.Where(x => x.Id == systemId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedBy, x => x.CreatedBy), ct);
        if (locked == 0) return NotFound();

        var targets = await db.SecurityWebsiteTargets.Where(x => x.SystemId == systemId).ToListAsync(ct);
        var legacySection = SecurityValues.LegacySection(system.Kind);
        if (targets.Count == 0 && section != legacySection && !string.IsNullOrWhiteSpace(system.TargetUrl))
        {
            db.SecurityWebsiteTargets.Add(new SecurityWebsiteTarget
            {
                SystemId = systemId, Section = legacySection, TargetUrl = system.TargetUrl,
                Enabled = true, RequiresLogin = system.RequiresLogin, AuthProfile = system.AuthProfile
            });
        }

        var target = targets.SingleOrDefault(x => x.Section == section);
        if (target == null)
        {
            target = new SecurityWebsiteTarget { SystemId = systemId, Section = section };
            db.SecurityWebsiteTargets.Add(target);
        }

        // Keep an empty, disabled row so the legacy URL cannot restore a removed section.
        target.Enabled = false;
        target.RequiresLogin = false;
        target.TargetUrl = "";
        target.AuthProfile = null;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        TempData["Success"] = $"ลบ {SecurityValues.SectionLabel(section)} แล้ว";
        return RedirectToAction(nameof(Index), new { projectId = system.ProjectId, workflow = SelectedWorkflow });
    }

    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.ApproveHosts")]
    public async Task<IActionResult> ApproveWebsiteTarget(long targetId,bool approved,CancellationToken ct)
    {
        var target=await db.SecurityWebsiteTargets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==targetId,ct);
        if(target==null)return NotFound();
        var system=await db.SecuritySystemRows.AsNoTracking().SingleAsync(x=>x.Id==target.SystemId,ct);
        if(await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        try{await scans.SetHostApprovalAsync(system.ProjectId,SecurityScanService.TargetHost(target.TargetUrl),approved,UserId,ct);TempData["Success"]=approved?"อนุมัติโดเมนแล้ว":"ถอนอนุมัติโดเมนแล้ว";}
        catch(InvalidOperationException error){TempData["Error"]=error.Message;}
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }

    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> SaveLogin(long systemId,bool enabled,string? loginUrl,string? username,string? password,string? loggedInRegex,string? requestData,CancellationToken ct,string authenticationType="Form",string tokenPath="token",string? scanStartUrl=null)
    {
        Response.Headers.CacheControl="no-store";
        var system=await db.SecuritySystemRows.SingleOrDefaultAsync(x=>x.Id==systemId,ct);
        if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(SecurityValues.IsMobile(system.Kind))return BadRequest();
        string? profile=null;
        try{
            if(enabled){
                profile=await loginStore.SaveAsync(system.Id,new SecurityLoginCredentials{
                    AuthenticationType=authenticationType,TokenPath=tokenPath,ScanStartUrl=scanStartUrl?.Trim(),
                    LoginUrl=loginUrl?.Trim()??"",Username=username??"",Password=password??"",LoggedInRegex=loggedInRegex??"",
                    RequestData=string.IsNullOrWhiteSpace(requestData)?"username={%username%}&password={%password%}":requestData
                },system.TargetUrl,ct,system.AuthProfile);
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
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
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
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> AddSystem(int projectId,string name,string kind,string? targetUrl,bool requiresLogin,string? authProfile,CancellationToken ct,int? assignedEmpId=null,DateTime? startDate=null,DateTime? endDate=null,string workflow="Formal")
    {
        if(await AllowedProject(projectId,ct)==null)return NotFound();
        try{
            if(string.IsNullOrWhiteSpace(name)||name.Length>160||!SecurityValues.Kinds.Contains(kind))throw new InvalidOperationException("กรอกชื่อระบบและประเภทให้ถูกต้อง");
            if(!SecurityValues.IsMobile(kind))SecurityScanService.ValidateUrl(targetUrl);
            if(authProfile?.Length>80||authProfile?.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_')==true)throw new InvalidOperationException("ชื่อโปรไฟล์ใช้ได้เฉพาะตัวอักษรอังกฤษ ตัวเลข - และ _");
            if(requiresLogin&&string.IsNullOrWhiteSpace(authProfile))throw new InvalidOperationException("กรุณาระบุชื่อโปรไฟล์ Login บนเครื่องสแกน");
            if(workflow is not ("Formal" or "Development"))throw new InvalidOperationException("รูปแบบการทำงานไม่ถูกต้อง");
            SecurityIssueDefaults? issueDefaults=null;
            if(workflow=="Formal"){
            if(!Can("SecurityScans.CreateIssues")||!Can("ProjectIssues.Create"))throw new InvalidOperationException("ต้องมีสิทธิ์สร้าง Issue จากผลสแกน และ ProjectIssues.Create เพื่อกำหนดสร้าง Issue อัตโนมัติ");
            if(!assignedEmpId.HasValue||!await db.Employees.AnyAsync(x=>x.EmpId==assignedEmpId&&x.Status=="ACTIVE",ct))throw new InvalidOperationException("เลือกผู้รับผิดชอบที่ยังใช้งาน");
            if(!startDate.HasValue||!endDate.HasValue||startDate.Value==default||endDate.Value.Date<startDate.Value.Date)throw new InvalidOperationException("ระบุวันที่เริ่มและสิ้นสุดการแก้ไข โดยสิ้นสุดต้องไม่ก่อนวันเริ่ม");
            issueDefaults=new(){AssignedEmpId=assignedEmpId.Value,StartDate=startDate.Value.Date,EndDate=endDate.Value.Date,CreatedBy=UserId,CreatorEmpId=await EmployeeId(ct)};
            }
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            var system=new SecuritySystem{Workflow=workflow,ProjectId=projectId,Name=name.Trim(),Kind=kind,TargetUrl=SecurityValues.IsMobile(kind)?null:targetUrl?.Trim(),RequiresLogin=requiresLogin,AuthProfile=authProfile,CreatedBy=UserId};
            db.SecuritySystemRows.Add(system);await db.SaveChangesAsync(ct);
            if(issueDefaults!=null){issueDefaults.SystemId=system.Id;db.SecurityIssueDefaults.Add(issueDefaults);}
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);TempData["Success"]=workflow=="Development"?"เพิ่มระบบสำหรับสแกนระหว่างพัฒนาแล้ว ไม่สร้าง Issue":"เพิ่มระบบแล้ว ผลสแกน Critical / High / Medium / Low จะสร้าง Issue อัตโนมัติ";
        }catch(InvalidOperationException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Index),new{projectId,workflow=SelectedWorkflow});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit"),RequestSizeLimit(268435456),RequestFormLimits(MultipartBodyLengthLimit=268435456)]
    public async Task<IActionResult> AddVersion(long systemId,string versionName,string? buildNumber,string? gitCommit,string environment,string? changeNotes,IFormFile? application,CancellationToken ct)
    {
        var system=await db.SecuritySystemRows.SingleOrDefaultAsync(x=>x.Id==systemId,ct);if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(system.Workflow!=SelectedWorkflow)return BadRequest("ระบบอยู่คนละรูปแบบการทำงาน");
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
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> Queue(long versionId,string mode,string requestKey,CancellationToken ct,string workflow="Formal")
    {
        var version=await db.SecurityVersionRows.FindAsync([versionId],ct);if(version==null)return NotFound();var system=await db.SecuritySystemRows.FindAsync([version.SystemId],ct);if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        try{
            if(!SecurityValues.IsMobile(system.Kind)){
                var ids=await scans.QueueWebsiteAsync(versionId,mode,requestKey,UserId,ct,workflow);
                TempData["Success"]=$"เพิ่ม Website เข้าคิวแล้ว {ids.Count} ส่วน ตรวจผลแต่ละส่วนได้ในประวัติรอบสแกน";
                return Redirect(Url.Action(nameof(Index),new{projectId=system.ProjectId,workflow})+"#security-history");
            }
            var id=await scans.QueueAsync(versionId,mode,requestKey,UserId,ct,workflow:workflow);return RedirectToAction(nameof(Scan),new{id});}catch(InvalidOperationException e){TempData["Error"]=e.Message;return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});}
    }
    public async Task<IActionResult> Scan(long id,long? baselineId,CancellationToken ct,int page=1)
    {
        var scope=await Scope(id,ct);if(scope==null)return NotFound();var (scan,version,system,project)=scope.Value;
        var run=await SecurityScanRun.LoadAsync(db,scan,ct);
        var runIds=run.Ids;
        var observationQuery=db.SecurityScanFindingRows.AsNoTracking().Where(x=>runIds.Contains(x.ScanId));
        var counts=await observationQuery.GroupBy(x=>x.Severity).Select(g=>new{Severity=g.Key,Count=g.Count()}).ToDictionaryAsync(x=>x.Severity,x=>x.Count,ct);
        var total=counts.Values.Sum();const int pageSize=50;
        var pageNumber=Math.Clamp(page,1,Math.Max(1,(int)Math.Ceiling(total/(double)pageSize)));
        var allFindingIds=await observationQuery.Select(x=>x.FindingId).ToListAsync(ct);
        var observations=await observationQuery.OrderByDescending(x=>x.Severity=="Critical"?5:x.Severity=="High"?4:x.Severity=="Medium"?3:x.Severity=="Low"?2:1).ThenBy(x=>x.Title).ThenBy(x=>x.Id).Skip((pageNumber-1)*pageSize).Take(pageSize).ToListAsync(ct);var fids=observations.Select(x=>x.FindingId).ToArray();var versions=await db.SecurityVersionRows.Where(x=>x.SystemId==system.Id&&x.Environment==version.Environment).Select(x=>x.Id).ToListAsync(ct);
        var websitePart=run.Parts.FirstOrDefault(x=>x.ScanId==id);
        var baselines=await SecurityScanComparison.Candidates(db.SecurityScanRows.AsNoTracking(),db.SecurityWebsiteScanParts.AsNoTracking(),versions.ToArray(),scan,scan.Engine=="ZAP"?websitePart?.Section??SecurityValues.LegacySection(system.Kind):null,SecurityValues.LegacySection(system.Kind)).ToListAsync(ct);
        SecurityScanRun? baselineRun=null;
        if(run.IsWebsiteBatch){
            var candidates=await db.SecurityScanRows.AsNoTracking().Where(x=>versions.Contains(x.VersionId)&&x.Workflow==scan.Workflow&&x.Engine==scan.Engine&&x.Mode==scan.Mode).ToListAsync(ct);
            var candidateIds=candidates.Select(x=>x.Id).ToArray();
            var candidateParts=await db.SecurityWebsiteScanParts.AsNoTracking().Where(x=>candidateIds.Contains(x.ScanId)).ToListAsync(ct);
            var eligible=SecurityScanRun.Group(candidates,candidateParts).Where(x=>x.Scan.Id!=run.Scan.Id&&x.IsWebsiteBatch&&x.Scans.All(s=>s.Status=="Completed")&&x.ComparisonKey()==run.ComparisonKey()).Take(50).ToList();
            baselines=eligible.Select(x=>x.Scan).ToList();
            baselineRun=eligible.FirstOrDefault(x=>x.Scan.Id==baselineId);
        }else{
            var candidateIds=baselines.Select(x=>x.Id).ToArray();
            var batchScanIds=await db.SecurityWebsiteScanParts.Where(x=>candidateIds.Contains(x.ScanId)).Select(x=>x.ScanId).ToListAsync(ct);
            baselines=baselines.Where(x=>!batchScanIds.Contains(x.Id)).ToList();
            if(baselines.FirstOrDefault(x=>x.Id==baselineId) is {} chosen)baselineRun=await SecurityScanRun.LoadAsync(db,chosen,ct);
        }
        var baselineIds=baselineRun?.Ids??Array.Empty<long>();
        var baselineFindings=baselineRun==null?null:await db.SecurityScanFindingRows.AsNoTracking().Where(x=>baselineIds.Contains(x.ScanId)).Select(x=>new SecurityScanFinding{FindingId=x.FindingId}).ToListAsync(ct);
        var loginChanged=baselineRun!=null&&run.Scans.Any(current=>baselineRun.Scans.Any(old=>baselineRun.Section(old.Id)==run.Section(current.Id)&&old.AuthProfile!=current.AuthProfile));
        var runProgress=run.IsWebsiteBatch?ScanProgressPage.FromRun(run,await db.SecurityScanProgressRows.AsNoTracking().Where(x=>runIds.Contains(x.ScanId)).ToListAsync(ct)):null;
        return View(new SecurityScanPage{Run=run,RunProgress=runProgress,LoginComparisonChanged=loginChanged,WebsitePart=websitePart,WebsiteBatchParts=run.Parts.ToList(),TotalObservations=total,PageNumber=pageNumber,PageSize=pageSize,BaselineId=baselineId,SeverityCounts=counts,AllFindingIds=allFindingIds,Progress=await db.SecurityScanProgressRows.AsNoTracking().SingleOrDefaultAsync(x=>x.ScanId==id,ct),Project=project,System=system,Version=version,Scan=run.Scan,Observations=observations,Findings=await db.SecurityFindingRows.AsNoTracking().Where(x=>fids.Contains(x.Id)).ToListAsync(ct),History=await db.SecurityFindingHistoryRows.AsNoTracking().Where(x=>fids.Contains(x.FindingId)).OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync(ct),Links=await db.SecurityFindingIssueLinkRows.AsNoTracking().Where(x=>fids.Contains(x.FindingId)&&db.ProjectIssues.Any(issue=>issue.IssueId==x.IssueId&&issue.ProjectId==project.ProjectId)).ToListAsync(ct),Employees=await db.Employees.AsNoTracking().Where(x=>x.Status=="ACTIVE").OrderBy(x=>x.EmpName).ToListAsync(ct),LinkedIssues=await db.ProjectIssues.AsNoTracking().Where(issue=>issue.ProjectId==project.ProjectId&&db.SecurityFindingIssueLinkRows.Any(link=>link.IssueId==issue.IssueId&&fids.Contains(link.FindingId))).ToListAsync(ct),ExistingIssues=await db.ProjectIssues.AsNoTracking().Where(x=>x.ProjectId==project.ProjectId).OrderByDescending(x=>x.IssueId).Take(200).ToListAsync(ct),Baselines=baselines,BaselineFindings=baselineFindings,CanManage=Can("SecurityScans.Edit"),CanCreateIssue=scan.Workflow=="Formal"&&Can("SecurityScans.CreateIssues")&&Can("ProjectIssues.Create")});
    }
    [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Progress(long id,CancellationToken ct)
    {
        var scope=await Scope(id,ct);if(scope==null)return NotFound();
        var run=await SecurityScanRun.LoadAsync(db,scope.Value.Scan,ct);
        var ids=run.Ids;
        var progress=await db.SecurityScanProgressRows.AsNoTracking().Where(x=>ids.Contains(x.ScanId)).ToListAsync(ct);
        return Json(run.IsWebsiteBatch?ScanProgressPage.FromRun(run,progress):ScanProgressPage.From(scope.Value.Scan,progress.FirstOrDefault()));
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> EditSystem(long systemId,string name,string? targetUrl,CancellationToken ct)
    {
        var system=await db.SecuritySystemRows.SingleOrDefaultAsync(x=>x.Id==systemId,ct);
        if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(system.Workflow!=SelectedWorkflow)return BadRequest();
        try{
            if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>160)throw new InvalidOperationException("กรอกชื่อระบบไม่เกิน 160 ตัวอักษร");
            if(!SecurityValues.IsMobile(system.Kind))SecurityScanService.ValidateUrl(targetUrl);
            system.Name=name.Trim();system.TargetUrl=SecurityValues.IsMobile(system.Kind)?null:targetUrl?.Trim();
            await db.SaveChangesAsync(ct);TempData["Success"]="แก้ไขระบบแล้ว กรุณาตรวจ URL และ Login ของแต่ละส่วนก่อนสแกนรอบใหม่";
        }catch(InvalidOperationException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }

    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> EditVersion(long versionId,string versionName,string? buildNumber,string? gitCommit,string environment,string? changeNotes,CancellationToken ct)
    {
        var version=await db.SecurityVersionRows.SingleOrDefaultAsync(x=>x.Id==versionId,ct);
        if(version==null)return NotFound();
        var system=await db.SecuritySystemRows.SingleAsync(x=>x.Id==version.SystemId,ct);
        if(await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(system.Workflow!=SelectedWorkflow)return BadRequest();
        try{
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            await db.SecurityVersionRows.Where(x=>x.Id==versionId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.CreatedBy,v=>v.CreatedBy),ct);
            if(string.IsNullOrWhiteSpace(versionName)||versionName.Length>100||buildNumber?.Length>100||gitCommit?.Length>64||changeNotes?.Length>4000||environment is not ("UAT" or "Production"))throw new InvalidOperationException("ข้อมูลเวอร์ชันไม่ถูกต้อง");
            if(environment!=version.Environment&&await db.SecurityScanRows.AnyAsync(x=>x.VersionId==versionId,ct))throw new InvalidOperationException("เวอร์ชันมีประวัติสแกนแล้ว ไม่สามารถเปลี่ยนสภาพแวดล้อมได้ กรุณาเพิ่มเวอร์ชันใหม่");
            version.VersionName=versionName.Trim();version.BuildNumber=buildNumber?.Trim();version.GitCommit=gitCommit?.Trim();version.Environment=environment;version.ChangeNotes=changeNotes?.Trim();
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);TempData["Success"]="แก้ไขเวอร์ชันแล้ว";
        }catch(InvalidOperationException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }

    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Delete")]
    public async Task<IActionResult> DeleteVersion(long versionId,CancellationToken ct)
    {
        var version=await db.SecurityVersionRows.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==versionId,ct);
        if(version==null)return NotFound();
        var system=await db.SecuritySystemRows.AsNoTracking().SingleAsync(x=>x.Id==version.SystemId,ct);
        if(await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(system.Workflow!=SelectedWorkflow)return BadRequest();
        IReadOnlyList<string> files;
        try{files=await scans.DeleteVersionAsync(versionId,ct);}
        catch(InvalidOperationException e){TempData["Error"]=e.Message;return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});}
        var cleanupFailed=false;
        foreach(var file in files.Distinct())try{storage.Delete(file);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException){cleanupFailed=true;logger?.LogWarning(e,"Could not remove file for deleted version {VersionId}",versionId);}
        TempData[cleanupFailed?"Error":"Success"]=cleanupFailed?"ลบเวอร์ชันแล้ว แต่ลบไฟล์บางไฟล์ไม่ได้ กรุณาให้ผู้ดูแลตรวจสอบ":"ลบเวอร์ชัน ไฟล์ และประวัติสแกนแล้ว โดยเก็บ Issue และประเด็นติดตามเดิมไว้";
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }

    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Delete")]
    public async Task<IActionResult> DeleteSystem(long systemId,CancellationToken ct)
    {
        var system=await db.SecuritySystemRows.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==systemId,ct);
        if(system==null||await AllowedProject(system.ProjectId,ct)==null)return NotFound();
        if(system.Workflow!=SelectedWorkflow)return BadRequest();
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        await db.SecuritySystemRows.Where(x=>x.Id==systemId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Name,x=>x.Name),ct);
        var versionIds=await db.SecurityVersionRows.Where(x=>x.SystemId==systemId).Select(x=>x.Id).ToListAsync(ct);
        var scanIds=await db.SecurityScanRows.Where(x=>versionIds.Contains(x.VersionId)).Select(x=>x.Id).ToListAsync(ct);
        // Lock scans against worker claims before checking their terminal state.
        await db.SecurityScanRows.Where(x=>scanIds.Contains(x.Id)).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,x=>x.Status),ct);
        if(await db.SecurityScanRows.AnyAsync(x=>scanIds.Contains(x.Id)&&(x.Status=="Queued"||x.Status=="Running"),ct)){
            TempData["Error"]="ระบบมีงานในคิวหรือกำลังสแกน กรุณายกเลิกหรือรอให้เสร็จก่อนลบ";
            return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
        }
        var files=await db.SecurityArtifactRows.Where(x=>versionIds.Contains(x.VersionId)).Select(x=>x.StorageKey).ToListAsync(ct);
        files.AddRange(await db.SecurityReportRows.Where(x=>scanIds.Contains(x.ScanId)).Select(x=>x.StorageKey).ToListAsync(ct));
        var profiles=await db.SecurityWebsiteTargets.Where(x=>x.SystemId==systemId).Select(x=>x.AuthProfile).ToListAsync(ct);
        profiles.Add(system.AuthProfile);
        profiles.AddRange(await db.SecurityScanRows.Where(x=>scanIds.Contains(x.Id)).Select(x=>x.AuthProfile).ToListAsync(ct));
        var findingIds=await db.SecurityFindingRows.Where(x=>x.SystemId==systemId).Select(x=>x.Id).ToListAsync(ct);
        await db.SecurityWebsiteScanParts.Where(x=>scanIds.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityScanProgressRows.Where(x=>scanIds.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityScanFindingRows.Where(x=>scanIds.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityReportRows.Where(x=>scanIds.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityScanRows.Where(x=>scanIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.SecurityFindingHistoryRows.Where(x=>findingIds.Contains(x.FindingId)).ExecuteDeleteAsync(ct);
        await db.SecurityFindingIssueLinkRows.Where(x=>findingIds.Contains(x.FindingId)).ExecuteDeleteAsync(ct);
        await db.SecurityFindingRows.Where(x=>findingIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.SecurityArtifactRows.Where(x=>versionIds.Contains(x.VersionId)).ExecuteDeleteAsync(ct);
        await db.SecurityVersionRows.Where(x=>x.SystemId==systemId).ExecuteDeleteAsync(ct);
        await db.SecurityWebsiteTargets.Where(x=>x.SystemId==systemId).ExecuteDeleteAsync(ct);
        await db.SecurityIssueDefaults.Where(x=>x.SystemId==systemId).ExecuteDeleteAsync(ct);
        await db.SecuritySystemRows.Where(x=>x.Id==systemId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        files.AddRange(profiles.Where(SecurityLoginStore.IsManaged).Select(x=>x![4..]+".login"));
        var cleanupFailed=false;
        foreach(var key in files.Distinct())try{storage.Delete(key);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){cleanupFailed=true;logger?.LogWarning(ex,"Could not remove file for deleted security system {SystemId}",systemId);}
        logger?.LogInformation("Security system {SystemId} deleted by user {UserId}",systemId,UserId);
        TempData[cleanupFailed?"Error":"Success"]=cleanupFailed?"ลบระบบแล้ว แต่ลบไฟล์บางไฟล์ไม่ได้ กรุณาให้ผู้ดูแลตรวจสอบ":"ลบระบบ เวอร์ชัน และประวัติสแกนแล้ว โดยเก็บ Issue เดิมไว้";
        return RedirectToAction(nameof(Index),new{projectId=system.ProjectId,workflow=SelectedWorkflow});
    }

    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Delete")]
    public async Task<IActionResult> DeleteScan(long id,CancellationToken ct)
    {
        var scope=await Scope(id,ct);
        if(scope==null)return NotFound();
        var projectId=scope.Value.Project.ProjectId;
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var run=await SecurityScanRun.LoadAsync(db,scope.Value.Scan,ct);
        var ids=run.Ids;
        await db.SecurityScanRows.Where(x=>ids.Contains(x.Id)).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.Status,x=>x.Status),ct);
        if(await db.SecurityScanRows.AnyAsync(x=>ids.Contains(x.Id)&&x.Status!="Completed"&&x.Status!="Failed"&&x.Status!="Cancelled",ct)){
            TempData["Error"]="ไม่สามารถลบรอบที่อยู่ในคิวหรือกำลังสแกนได้ กรุณายกเลิกงานก่อน";return RedirectToAction(nameof(Index),new{projectId,workflow=SelectedWorkflow});
        }
        var reports=await db.SecurityReportRows.Where(x=>ids.Contains(x.ScanId)).Select(x=>x.StorageKey).ToListAsync(ct);
        await db.SecurityWebsiteScanParts.Where(x=>ids.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityScanProgressRows.Where(x=>ids.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityScanFindingRows.Where(x=>ids.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityReportRows.Where(x=>ids.Contains(x.ScanId)).ExecuteDeleteAsync(ct);
        await db.SecurityScanRows.Where(x=>ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        var fileCleanupFailed=false;
        foreach(var key in reports){
            try{storage.Delete(key);}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){fileCleanupFailed=true;logger?.LogWarning(ex,"Could not remove report file for deleted security scan {ScanId}",id);}
        }
        logger?.LogInformation("Security scan {ScanId} deleted by user {UserId} in project {ProjectId}",id,UserId,projectId);
        TempData[fileCleanupFailed?"Error":"Success"]=fileCleanupFailed?"ลบประวัติแล้ว แต่ลบไฟล์รายงานบางไฟล์ไม่ได้ กรุณาให้ผู้ดูแลตรวจสอบ":"ลบประวัติรอบสแกนแล้ว โดยเก็บเวอร์ชัน ไฟล์แอป และ Issue เดิมไว้";
        return RedirectToAction(nameof(Index),new{projectId,workflow=SelectedWorkflow});
    }

    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> Cancel(long id,CancellationToken ct)
    {
        var scope=await Scope(id,ct);if(scope==null)return NotFound();var run=await SecurityScanRun.LoadAsync(db,scope.Value.Scan,ct);var ids=run.Ids;await db.SecurityScanRows.Where(x=>ids.Contains(x.Id)&&(x.Status=="Queued"||x.Status=="Running"||x.Status=="Processing")).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Cancelled").SetProperty(x=>x.FinishedAt,DateTime.UtcNow),ct);return RedirectToAction(nameof(Scan),new{id});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.CreateIssues"),RequireMenu("ProjectIssues.Create")]
    public async Task<IActionResult> CreateIssues(long scanId,long[] observationIds,int employeeId,DateTime startDate,DateTime endDate,CancellationToken ct)
    {
        var scope=await Scope(scanId,ct);if(scope==null)return NotFound();
        var run=await SecurityScanRun.LoadAsync(db,scope.Value.Scan,ct);var runIds=run.Ids;
        var ids=observationIds.Distinct().Take(51).ToArray();
        var completed=0;
        try{
            if(ids.Length==0)throw new InvalidOperationException("กรุณาเลือกรายการผลสแกนอย่างน้อย 1 รายการก่อนสร้าง Issue");
            if(ids.Length>50)throw new InvalidOperationException("เลือกสร้าง Issue ได้ครั้งละไม่เกิน 50 รายการ");
            if(await db.SecurityScanFindingRows.CountAsync(x=>runIds.Contains(x.ScanId)&&ids.Contains(x.Id),ct)!=ids.Length)return BadRequest();
            if(startDate==default||endDate==default)throw new InvalidOperationException("กรุณาระบุวันเริ่มและกำหนดส่ง");
            var creator=await EmployeeId(ct);
            foreach(var id in ids){await scans.CreateIssueAsync(id,employeeId,startDate,endDate,UserId,creator,ct);completed++;}
            TempData["Success"]=$"สร้างหรือเชื่อม Issue สำเร็จ {completed} รายการ";
        }catch(InvalidOperationException e){TempData["Error"]=$"ดำเนินการสำเร็จแล้ว {completed} รายการ · "+e.Message;}
        catch(DbUpdateException error){
            logger?.LogError(error,"Security scan {ScanId} Issue conversion failed after {Completed} items. Reference {TraceId}",scanId,completed,HttpContext.TraceIdentifier);
            TempData["Error"]=$"บันทึก Issue ไม่สำเร็จจากฐานข้อมูล (สำเร็จแล้ว {completed} รายการ) กรุณาตรวจ Log ของเว็บ อ้างอิง {HttpContext.TraceIdentifier}";
        }
        return RedirectToAction(nameof(Scan),new{id=scanId});
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.Edit")]
    public async Task<IActionResult> Review(long observationId,string status,string note,int? assignedEmpId,DateTime? dueDate,CancellationToken ct)
    {
        var observation=await db.SecurityScanFindingRows.FindAsync([observationId],ct);if(observation==null||await Scope(observation.ScanId,ct)==null)return NotFound();
        return StatusCode(StatusCodes.Status409Conflict,"ข้อมูลติดตามผลสแกนแก้ไขได้จาก Issue ที่เชื่อมไว้เท่านั้น");
    }
    [HttpPost,ValidateAntiForgeryToken,RequireMenu("SecurityScans.CreateIssues"),RequireMenu("ProjectIssues.Create")]
    public async Task<IActionResult> LinkIssue(long observationId,int issueId,CancellationToken ct)
    {
        var observation=await db.SecurityScanFindingRows.FindAsync([observationId],ct);if(observation==null)return NotFound();var scope=await Scope(observation.ScanId,ct);if(scope==null)return NotFound();
        if(scope.Value.Scan.Workflow=="Development")return BadRequest("รอบสแกนระหว่างพัฒนาไม่เชื่อม Issue");
        if(!await db.ProjectIssues.AnyAsync(x=>x.IssueId==issueId&&x.ProjectId==scope.Value.Project.ProjectId,ct))return BadRequest();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.SecurityFindingRows.Where(x=>x.Id==observation.FindingId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastSeenAt,x=>x.LastSeenAt),ct);
        if(!await db.SecurityFindingIssueLinkRows.AnyAsync(x=>x.FindingId==observation.FindingId,ct)){
            db.SecurityFindingIssueLinkRows.Add(new(){FindingId=observation.FindingId,IssueId=issueId,CreatedBy=UserId});await db.SaveChangesAsync(ct);
            var issue=await db.ProjectIssues.AsNoTracking().SingleAsync(x=>x.IssueId==issueId,ct);
            await SecurityIssueWorkflow.StageStatusAsync(db,issue,SecurityIssueWorkflow.CurrentReviewStatus(issue),UserId);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return RedirectToAction(nameof(Scan),new{id=observation.ScanId});
    }
    public async Task<IActionResult> Report(long id,string format="json",CancellationToken ct=default)
    {
        var scope=await Scope(id,ct);if(scope==null)return NotFound();
        var run=await SecurityScanRun.LoadAsync(db,scope.Value.Scan,ct);
        if(!run.HasResults)return NotFound();
        var ids=run.Ids;
        if(format=="json"){
            if(!run.IsWebsiteBatch){var report=await db.SecurityReportRows.AsNoTracking().SingleOrDefaultAsync(x=>x.ScanId==id&&x.Format=="json",ct);if(report==null)return NotFound();return PhysicalFile(storage.PathFor(report.StorageKey),"application/json",$"security-scan-{id}.json");}
            var reports=await db.SecurityReportRows.AsNoTracking().Where(x=>ids.Contains(x.ScanId)&&x.Format=="json").ToListAsync(ct);
            var sections=new List<object>();
            foreach(var scan in run.Scans){
                System.Text.Json.JsonElement? original=null;
                if(scan.Status=="Completed"){
                    var report=reports.SingleOrDefault(x=>x.ScanId==scan.Id);if(report==null)return NotFound();
                    await using var input=System.IO.File.OpenRead(storage.PathFor(report.StorageKey));
                    using var document=await System.Text.Json.JsonDocument.ParseAsync(input,cancellationToken:ct);original=document.RootElement.Clone();
                }
                sections.Add(new{section=run.Section(scan.Id),scanId=scan.Id,status=scan.Status,targetUrl=scan.TargetUrl,error=scan.Error,report=original});
            }
            return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{scanId=run.Scan.Id,status=run.Scan.Status,sections}),"application/json",$"security-scan-{run.Scan.Id}.json");
        }
        if(format is not ("pdf" or "md"))return BadRequest();
        var items=await db.SecurityScanFindingRows.AsNoTracking().Where(x=>ids.Contains(x.ScanId)).ToListAsync(ct);
        var sectionNames=run.Parts.ToDictionary(x=>x.ScanId,x=>SecurityValues.SectionLabel(x.Section));
        if(format=="md")return File(System.Text.Encoding.UTF8.GetBytes(SecurityMarkdownReport.Create(run.Scan,scope.Value.Version,scope.Value.System,scope.Value.Project.ProjectDisplayName,items,sectionNames)),"text/markdown; charset=utf-8",$"security-scan-{run.Scan.Id}.md");
        var font=Path.Combine(env.WebRootPath,"fonts","THSarabunNew.ttf");if(System.IO.File.Exists(font)){using var stream=System.IO.File.OpenRead(font);QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("SecurityThai",stream);}
        var pdf=QuestPDF.Fluent.Document.Create(container=>container.Page(page=>{page.Size(PageSizes.A4);page.Margin(28);page.DefaultTextStyle(x=>x.FontSize(12).FontFamily(System.IO.File.Exists(font)?"SecurityThai":"Lato"));page.Content().Column(column=>{column.Item().Text($"Security Scan #{run.Scan.Id}").FontSize(22).Bold();column.Item().Text(scope.Value.Project.ProjectDisplayName);column.Item().Text($"สถานะ: {SecurityValues.StatusLabel(run.Scan.Status)}");column.Item().Text($"{scope.Value.System.Name} / {scope.Value.Version.VersionName} / {scope.Value.Version.Environment}\n{run.Scan.Engine} {run.Scan.EngineVersion}\n{run.Scan.Coverage}\nเวลาไทย: {SecurityDisplayDate.Utc(run.Scan.FinishedAt)}");foreach(var item in items.OrderByDescending(x=>SecurityValues.Rank(x.Severity))){column.Item().PaddingTop(16).Text($"[{item.Severity}] {item.Title}").Bold();if(sectionNames.TryGetValue(item.ScanId,out var section))column.Item().Text(section);column.Item().Text($"{item.Location}\n{item.Description}\nEvidence: {item.Evidence}\nRemediation: {item.Remediation}\nReferences: {item.References}");}});page.Footer().AlignRight().Text(t=>{t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});})).GeneratePdf();
        return File(pdf,"application/pdf",$"security-scan-{id}.pdf");
    }
}
