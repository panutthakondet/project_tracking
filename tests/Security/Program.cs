using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;
using ProjectTracking.SecurityWorker;
using System.Net;
using System.Text;
void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
async Task Reject(Func<Task> action,string label){try{await action();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception(label);}
var markdownItems=Enumerable.Range(1,51).Select(i=>new SecurityScanFinding{Id=i,Title=$"ผลตรวจ {i}",Severity=i==51?"High":"Low",Evidence="```\n<script>alert(1)</script>\n```",Remediation="แก้ไขให้ปลอดภัย"}).ToList();
var markdown=SecurityMarkdownReport.Create(new SecurityScan{Id=99,Engine="MobSF",Mode="Static"},new SecurityVersion{VersionName="1.0",Environment="UAT"},new SecuritySystem{Name="แอปทดสอบ"},"โครงการทดสอบ",markdownItems);
Check(markdown.Contains("51 รายการ")&&markdown.IndexOf("ผลตรวจ 51",StringComparison.Ordinal)<markdown.IndexOf("ผลตรวจ 1\n",StringComparison.Ordinal)&&markdown.Contains("ผลตรวจ 50")&&markdown.Contains("แก้ไขให้ปลอดภัย"),"Markdown exports all findings beyond a page with severity ordering and Thai text");
Check(markdown.Contains("````text\n```\n<script>")&&!markdown.Contains("LeaseToken"),"Markdown fences scanner content safely without worker credentials");
const string report="""{"alerts":[{"pluginId":"100","alert":"Missing header","risk":"Medium","url":"https://uat.example.com/account","description":"<p>Missing protection</p>","solution":"Set header","evidence":"sample"}]}""";
var root=Path.Combine(Path.GetTempPath(),"security-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try{
var persistentSettings=Path.Combine(root,"server-settings.json");
await File.WriteAllTextAsync(persistentSettings,System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,string>{{"SECURITY_WORKER_KEY",new string('a',64)},{"SECURITY_STORAGE_PATH",Path.Combine(root,"private")},{"SECURITY_AUTO_CREATE_SCHEMA","false"},{"ConnectionStrings:DefaultConnection","must-not-import"}}));
var persistentConfig=new ConfigurationManager();
SecurityServerSettings.Load(persistentConfig,persistentSettings);
Check(persistentConfig["SECURITY_STORAGE_PATH"]==Path.Combine(root,"private")&&persistentConfig["ConnectionStrings:DefaultConnection"]==null,"persistent server file imports scanner settings only");
var republishedConfig=new ConfigurationManager();
SecurityServerSettings.Load(republishedConfig,persistentSettings);
Check(republishedConfig["SECURITY_WORKER_KEY"]==persistentConfig["SECURITY_WORKER_KEY"]&&File.Exists(persistentSettings),"fresh published configuration retains the same external worker key");
SecurityServerSettings.Load(new ConfigurationManager(),Path.Combine(root,"absent.json"));
await File.WriteAllTextAsync(persistentSettings,"{invalid-json");
await Reject(()=>Task.Run(()=>SecurityServerSettings.Load(new ConfigurationManager(),persistentSettings)),"malformed persistent settings fail closed");
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"SECURITY_WORKER_KEY",new string('x',48)},{"SECURITY_STORAGE_PATH",Path.Combine(root,"private")}}).Build();
var storage=new SecurityStorage(config,new TestEnvironment(root));
using(var services=new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config).BuildServiceProvider()){
 var http=new Microsoft.AspNetCore.Http.DefaultHttpContext{RequestServices=services};
 var auth=new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(new Microsoft.AspNetCore.Mvc.ActionContext(http,new Microsoft.AspNetCore.Routing.RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),[]);
 var filter=new SecurityWorkerKeyAttribute();filter.OnAuthorization(auth);Check(auth.Result is Microsoft.AspNetCore.Mvc.UnauthorizedResult,"worker API rejects requests without its key");
 http.Request.Headers.Authorization="Bearer "+new string('x',48);auth.Result=null;filter.OnAuthorization(auth);Check(auth.Result==null,"worker API accepts the matching key");
}

await Reject(()=>Task.Run(()=>storage.PathFor("../secret")),"storage rejects traversal");
await Reject(()=>storage.WriteAsync(new MemoryStream([1,2,3]),".json",2,default),"oversized upload rejected");Check(Directory.GetFiles(Path.Combine(root,"private")).Length==0,"failed upload leaves no file");
using var conn=new SqliteConnection("DataSource=:memory:");await conn.OpenAsync();
await using var db=new TestDb(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);await db.Database.EnsureCreatedAsync();
var system=new SecuritySystem{ProjectId=1,Name="Web",TargetUrl="https://uat.example.com/",CreatedBy=1};db.SecuritySystemRows.Add(system);await db.SaveChangesAsync();
var version=new SecurityVersion{SystemId=system.Id,VersionName="1",Environment="UAT",CreatedBy=1};db.SecurityVersionRows.Add(version);await db.SaveChangesAsync();
var service=new SecurityScanService(db,storage,config);await service.SetHostApprovalAsync(1,"uat.example.com",true,1,default);var request=Guid.NewGuid().ToString();var id=await service.QueueAsync(version.Id,"Passive",request,1,default);
Check(id==await service.QueueAsync(version.Id,"Passive",request,1,default),"queue submission is idempotent");
await Reject(()=>service.QueueAsync(version.Id,"Passive",request,2,default),"another user cannot reuse a request key");
var job=await service.ClaimAsync(default);Check(job?.Id==id&&await service.ClaimAsync(default)==null,"only one worker claims a queued run");
await Reject(()=>service.CompleteAsync(id,"wrong",report,new("test","test"),default),"wrong lease cannot submit");
await Reject(()=>service.CompleteAsync(id,job!.LeaseToken,"{}",new("test","test"),default),"malformed report is not treated as zero findings");
await service.CompleteAsync(id,job!.LeaseToken,report,new("test-engine","test coverage"),default);
await service.CompleteAsync(id,job.LeaseToken,report,new("test-engine","test coverage"),default);
Check(await db.SecurityReportRows.CountAsync()==1&&await db.SecurityScanFindingRows.CountAsync()==1,"result retry creates one immutable report");
var finding=await db.SecurityFindingRows.SingleAsync();finding.ReviewStatus="FixedPendingVerification";await db.SaveChangesAsync();
await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var again=await service.ClaimAsync(default);await service.CompleteAsync(again!.Id,again.LeaseToken,report,new("test","test"),default);
Check(await db.SecurityFindingRows.CountAsync()==1&&finding.ReviewStatus=="FixedPendingVerification","repeat scans retain finding identity and review state");
var employee=new Employee{EmpName="Tester",PositionId=1,DepartmentId=1};db.Employees.Add(employee);await db.SaveChangesAsync();
var observation=await db.SecurityScanFindingRows.OrderBy(x=>x.Id).FirstAsync();
var issue=await service.CreateIssueAsync(observation.Id,employee.EmpId,DateTime.Today,DateTime.Today.AddDays(1),1,employee.EmpId,default);
Check(issue==await service.CreateIssueAsync(observation.Id,employee.EmpId,DateTime.Today,DateTime.Today.AddDays(1),1,employee.EmpId,default)&&await db.ProjectIssues.CountAsync()==1&&await db.ProjectIssueStatusHistories.CountAsync()==1,"issue conversion deduplicates and records initial history");
var staleLink=await db.SecurityFindingIssueLinkRows.SingleAsync(x=>x.FindingId==finding.Id);
staleLink.IssueId=int.MaxValue;await db.SaveChangesAsync();
observation.Evidence=string.Concat(Enumerable.Repeat("หลักฐาน🔒",20000));await db.SaveChangesAsync();
var recoveredIssueId=await service.CreateIssueAsync(observation.Id,employee.EmpId,DateTime.Today,DateTime.Today.AddDays(1),1,employee.EmpId,default);
var recoveredIssue=await db.ProjectIssues.SingleAsync(x=>x.IssueId==recoveredIssueId);
Check(recoveredIssueId!=int.MaxValue&&(await db.SecurityFindingIssueLinkRows.SingleAsync(x=>x.FindingId==finding.Id)).IssueId==recoveredIssueId,"deleted or missing linked Issue can be recreated and linked to a persisted record");
Check(Encoding.UTF8.GetByteCount(recoveredIssue.IssueDetail!)<=60000&&recoveredIssue.IssueDetail!.Contains("/SecurityScans/Scan/")&&recoveredIssue.IssueDetail.Contains("ข้อความย่อ")&&!recoveredIssue.IssueDetail.Contains('\uFFFD'),"large Thai/emoji scan evidence fits MySQL TEXT without broken Unicode and links to the complete report");
Check(SecurityScanService.IssueDetailsForStorage("short details",1)=="short details","normal Issue details remain unchanged");
var sourceLinks=await SecurityIssueWorkflow.ScanLinksAsync(db,system.ProjectId,new[]{recoveredIssueId});
Check(sourceLinks.ContainsKey(recoveredIssueId),"Dev Issue link resolves a completed source scan");
Check((await SecurityIssueWorkflow.ScanLinksAsync(db,system.ProjectId+999,new[]{recoveredIssueId})).Count==0,"source scan links stay inside the Issue project");
foreach(var transition in new[]{("FIXED","FixedPendingVerification",true),("PASS","VerifiedFixed",false),("REJECT","AcceptedRisk",false),("FAIL","InProgress",false)}){
    var status=transition.Item3?SecurityIssueWorkflow.FromDevStatus(transition.Item1):SecurityIssueWorkflow.FromBaStatus(transition.Item1);
    Check(status==transition.Item2,"Issue status mapping: "+transition.Item1);
    if(transition.Item3)recoveredIssue.DevStatus=transition.Item1;else recoveredIssue.IssueStatus=transition.Item1;
    var previousHistory=await db.SecurityFindingHistoryRows.CountAsync();
    await SecurityIssueWorkflow.StageStatusAsync(db,recoveredIssue,status!,1);await db.SaveChangesAsync();
    Check((await db.SecurityFindingRows.AsNoTracking().SingleAsync(x=>x.Id==finding.Id)).ReviewStatus==status,"Issue status persists to linked security finding: "+status);
    await SecurityIssueWorkflow.StageStatusAsync(db,recoveredIssue,status!,1);await db.SaveChangesAsync();
    Check(await db.SecurityFindingHistoryRows.CountAsync()==previousHistory+1,"status history is not duplicated for the same state");
}
var unlinkedIssue=new ProjectIssue{IssueId=int.MaxValue,ProjectId=system.ProjectId};
var beforeUnlinked=await db.SecurityFindingHistoryRows.CountAsync();await SecurityIssueWorkflow.StageStatusAsync(db,unlinkedIssue,"VerifiedFixed",1);await db.SaveChangesAsync();
Check(await db.SecurityFindingHistoryRows.CountAsync()==beforeUnlinked,"ordinary Issues do not change security findings");
finding.ReviewStatus="FixedPendingVerification";await db.SaveChangesAsync();

await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var empty=await service.ClaimAsync(default);await service.CompleteAsync(empty!.Id,empty.LeaseToken,"{\"alerts\":[]}",new("test","test"),default);
Check(finding.ReviewStatus=="FixedPendingVerification","absence from a scan never auto marks fixed");
var prod=new SecurityVersion{SystemId=system.Id,VersionName="1-prod",Environment="Production"};db.SecurityVersionRows.Add(prod);await db.SaveChangesAsync();
await Reject(()=>service.QueueAsync(prod.Id,"Active",Guid.NewGuid().ToString(),1,default),"active scan blocked for Production");
await service.QueueAsync(prod.Id,"Passive",Guid.NewGuid().ToString(),1,default);var prodJob=await service.ClaimAsync(default);await service.CompleteAsync(prodJob!.Id,prodJob.LeaseToken,report,new("test","test"),default);
Check(await db.SecurityFindingRows.CountAsync()==2,"UAT and Production findings are separate");
await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var cancelled=await service.ClaimAsync(default);
await db.SecurityScanRows.Where(x=>x.Id==cancelled!.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Cancelled"));
Check(!await service.HeartbeatAsync(cancelled!.Id,cancelled.LeaseToken,default),"cancelled jobs cannot renew their lease");
await Reject(()=>service.CompleteAsync(cancelled.Id,cancelled.LeaseToken,report,new("test","test"),default),"cancelled jobs cannot publish results");

Check(SecurityReportParser.Parse("MobSF","""{"code_analysis":{"findings":{"rule":{"metadata":{"severity":"high","description":"Weak crypto","fix":"Use safe algorithm"},"files":{"src/app.java":"12"}}}}} """).Single().Severity=="High","MobSF code finding normalized");
await Reject(()=>Task.Run(()=>SecurityReportParser.Parse("MobSF","{\"error\":\"failed\"}")),"engine error cannot become a clean report");
Check(TargetPolicy.Forbidden(IPAddress.Parse("169.254.169.254"),true)&&TargetPolicy.Forbidden(IPAddress.Loopback,true)&&TargetPolicy.Forbidden(IPAddress.Parse("10.0.0.1"),false)&&!TargetPolicy.Forbidden(IPAddress.Parse("10.0.0.1"),true),"network policy blocks metadata and requires private target opt-in");
await Reject(()=>TargetPolicy.ValidateAsync("https://unapproved.example/","approved.example",false,default),"unapproved target fails before DNS or network scan");
await Reject(()=>TargetPolicy.ValidateAsync("https://93.184.216.34/","",false,default),"mobile-only configuration with an empty allowlist rejects every web target");
using var fake=new HttpClient(new FakeEngine()){BaseAddress=new Uri("http://zap/")};var engines=new ScannerEngines(fake,fake,_=>"");
await Reject(()=>engines.Web(new(1,"lease","WebInformation","ZAP","Passive","1",null,"UAT","https://unapproved.example/",false,null,null,null),default),"scanner refuses unapproved jobs");
await using var mysql=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySql("Server=localhost;Database=unused;User=unused",new MySqlServerVersion(new Version(8,0,36))).Options);
var sql=SecuritySchema.Statements(mysql).ToArray();Check(sql.Length==11&&sql.All(s=>s.StartsWith("CREATE TABLE IF NOT EXISTS `security_")),"DDL only creates eleven security tables including independent progress snapshots");
await File.WriteAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(),"deploy/security/schema.sql"),"-- Generated from SecurityModelConfiguration. New tables only.\n"+string.Join("\n\n",sql)+"\n");
var progressDdl=sql.Single(s=>s.StartsWith("CREATE TABLE IF NOT EXISTS `security_scan_progress`"));
Check(progressDdl.Contains("PRIMARY KEY (`ScanId`)")&&!progressDdl.Contains("AUTO_INCREMENT"),"progress schema uses existing scan ID without altering scan/report tables");
await File.WriteAllTextAsync("deploy/security/progress-schema.sql","-- Select the existing ProjectTracking database before running. Creates one new table only.\n"+progressDdl+"\n");

var hostDdl=sql.Single(s=>s.StartsWith("CREATE TABLE IF NOT EXISTS `security_host_approvals`"));
await File.WriteAllTextAsync("deploy/security/host-approvals-schema.sql","-- Select the existing ProjectTracking database before running. Creates one new table only.\n"+hostDdl+"\n");
await HostApprovalChecks.Run(db,service);
await ProgressChecks.Run(db,service,version.Id);
PermissionChecks.Run();
await ArtifactChecks.Run(db,service,storage);
await LargeReportChecks.Run();
await EngineChecks.Run();
await LoginChecks.Run(root);
await ReadinessChecks.Run();
await RazorChecks.Run();
Console.WriteLine("ALL SECURITY CHECKS PASSED — isolated SQLite and fake HTTP; no real scans or production writes.");
}finally{Directory.Delete(root,true);}
class FakeEngine:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>throw new Exception("Unexpected network request");}
class TestEnvironment(string root):IWebHostEnvironment
{
 public string ApplicationName{get;set;}="SecurityTests";public string EnvironmentName{get;set;}="Testing";public string ContentRootPath{get;set;}=root;public string WebRootPath{get;set;}=Path.Combine(root,"wwwroot");public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
class TestDb(DbContextOptions<AppDbContext> options):AppDbContext(options)
{
 protected override void OnModelCreating(ModelBuilder b){foreach(var e in b.Model.GetEntityTypes().ToArray())if(!e.ClrType.Name.StartsWith("Security",StringComparison.Ordinal)&&e.ClrType!=typeof(Employee)&&e.ClrType!=typeof(ProjectIssue)&&e.ClrType!=typeof(ProjectIssueStatusHistory))b.Ignore(e.ClrType);b.Entity<Employee>().Ignore(x=>x.PositionDefinition).Ignore(x=>x.Department).Ignore(x=>x.LoginUser);b.Entity<ProjectIssue>().Ignore(x=>x.Project).Ignore(x=>x.Images).Ignore(x=>x.FixImages);SecurityModelConfiguration.Configure(b);}
}
