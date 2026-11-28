using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;
public static class AutoIssueChecks
{
 public static async Task Run(string root){
  using var connection=new SqliteConnection("DataSource=:memory:");await connection.OpenAsync();
  await using var db=new TestDb(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["SECURITY_WORKER_KEY"]=new string('x',48),["SECURITY_STORAGE_PATH"]=Path.Combine(root,"auto-issues")}).Build();
  var service=new SecurityScanService(db,new SecurityStorage(config,new TestEnvironment(root)),config);
  var employee=new Employee{EmpName="Auto assignee",Status="ACTIVE",PositionId=1,DepartmentId=1};db.Employees.Add(employee);
  var system=new SecuritySystem{ProjectId=1,Name="Auto",Kind="Website",TargetUrl="https://example.com/"};db.SecuritySystemRows.Add(system);await db.SaveChangesAsync();
  var version=new SecurityVersion{SystemId=system.Id,VersionName="1",Environment="UAT"};db.SecurityVersionRows.Add(version);await db.SaveChangesAsync();
  var start=new DateTime(2026,12,1);var end=start.AddDays(7);
  db.SecurityIssueDefaults.Add(new(){SystemId=system.Id,AssignedEmpId=employee.EmpId,StartDate=start,EndDate=end,CreatedBy=1,CreatorEmpId=employee.EmpId});await db.SaveChangesAsync();
  await service.SetHostApprovalAsync(1,"example.com",true,1,default);
  var json=System.Text.Json.JsonSerializer.Serialize(new{alerts=new[]{"Critical","High","Medium","Low","Info"}.Select((severity,i)=>new{pluginId=i.ToString(),name="Finding "+severity,risk=severity,url="https://example.com/"+i,description="fixture",solution="fix"}).Concat(new[]{new{pluginId="0",name="Finding Critical",risk="Critical",url="https://example.com/other",description="fixture",solution="fix"}})});
  async Task Scan(){var id=await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var job=await service.ClaimAsync(default);await service.CompleteAsync(id,job!.LeaseToken,json,new("fake","fake"),default);await service.CompleteAsync(id,job.LeaseToken,json,new("fake","fake"),default);}
  await Scan();
  var issues=await db.ProjectIssues.ToListAsync();
  if(issues.Count!=4||issues.Any(x=>x.AssignTo!=employee.EmpId||x.StartDate!=start||x.EndDate!=end)||await db.SecurityFindingIssueLinkRows.CountAsync()!=5)throw new Exception("Automatic Issues must exclude Info and copy assignee/dates");
  var grouped=issues.Single(x=>x.IssueName.Contains("Finding Critical"));
  if(!grouped.IssueDetail!.Contains("https://example.com/0\nhttps://example.com/other")||
     await db.SecurityFindingIssueLinkRows.CountAsync(x=>x.IssueId==grouped.IssueId)!=2)
   throw new Exception("Grouped Issue must contain both locations and link both findings");
  var alternateLocation=await db.SecurityScanFindingRows.SingleAsync(x=>x.Location=="https://example.com/other");
  if(await service.CreateIssueAsync(alternateLocation.Id,employee.EmpId,start,end,1,employee.EmpId,default)!=grouped.IssueId)
   throw new Exception("Selecting another location must reuse the grouped Issue");
  await Scan();if(await db.ProjectIssues.CountAsync()!=4)throw new Exception("Repeated scan duplicated Issues");
  var completed=await db.SecurityScanRows.OrderBy(x=>x.Id).ToListAsync();
  completed[0].AuthProfile="web-old";completed[1].AuthProfile="web-new";
  await db.SaveChangesAsync();
  var comparison=await SecurityScanComparison.Candidates(db.SecurityScanRows,db.SecurityWebsiteScanParts,new[]{version.Id},completed[1],"WebInfo","WebInfo").ToListAsync();
  if(comparison.Count!=1||comparison[0].Id!=completed[0].Id)throw new Exception("Saving a new login profile must not hide comparable scans");
  db.SecurityWebsiteScanParts.Add(new(){ScanId=completed[0].Id,Section="WebAdmin",BatchKey="comparison-fixture"});await db.SaveChangesAsync();
  if(await SecurityScanComparison.Candidates(db.SecurityScanRows,db.SecurityWebsiteScanParts,new[]{version.Id},completed[1],"WebMember","WebInfo").AnyAsync())throw new Exception("Comparison mixed Website sections");
  db.SecurityWebsiteScanParts.RemoveRange(await db.SecurityWebsiteScanParts.ToListAsync());
  completed[0].AuthProfile=null;completed[1].AuthProfile=null;await db.SaveChangesAsync();
  Console.WriteLine("PASS comparison accepts renewed Login profiles and isolates Website sections");
  var devSystem=new SecuritySystem{ProjectId=1,Name="Auto",Kind="Website",TargetUrl="https://example.com/",Workflow="Development"};db.SecuritySystemRows.Add(devSystem);await db.SaveChangesAsync();
  var devVersion=new SecurityVersion{SystemId=devSystem.Id,VersionName="1",Environment="UAT"};db.SecurityVersionRows.Add(devVersion);await db.SaveChangesAsync();
  try{await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default,workflow:"Development");throw new Exception("Formal system queued as development");}catch(InvalidOperationException){}
  try{await service.QueueWebsiteAsync(devVersion.Id,"Passive",Guid.NewGuid().ToString(),1,default);throw new Exception("Development Website queued as Formal");}catch(InvalidOperationException){}
  if(await db.SecuritySystemRows.CountAsync(x=>x.Workflow=="Formal")!=1||await db.SecuritySystemRows.CountAsync(x=>x.Workflow=="Development")!=1)throw new Exception("System workflows were not separated");
  var devKey=Guid.NewGuid().ToString();
  var devId=await service.QueueAsync(devVersion.Id,"Passive",devKey,1,default,workflow:"Development");var devJob=await service.ClaimAsync(default);
  await service.CompleteAsync(devId,devJob!.LeaseToken,json,new("fake","development"),default);
  if(await db.ProjectIssues.CountAsync()!=4||await db.SecurityFindingRows.CountAsync()!=12||await db.SecurityScanRows.CountAsync(x=>x.Workflow=="Development")!=1||await db.SecurityScanRows.CountAsync(x=>x.Workflow=="Formal")!=2)throw new Exception("Development findings or history mixed with Formal / created Issues");
  var devObservation=await db.SecurityScanFindingRows.FirstAsync(x=>x.ScanId==devId);
  try{await service.CreateIssueAsync(devObservation.Id,employee.EmpId,start,end,1,employee.EmpId,default);throw new Exception("Development finding converted to Issue");}catch(InvalidOperationException){}
  try{await service.QueueAsync(version.Id,"Passive",devKey,1,default);throw new Exception("Request key changed workflow");}catch(InvalidOperationException){}
  Console.WriteLine("PASS Development scan separates history and finding identity; no automatic or manual Issues; request retry cannot change workflow");
  employee.Status="INACTIVE";await db.SaveChangesAsync();
  var failedId=await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var failedJob=await service.ClaimAsync(default);
  await service.CompleteAsync(failedId,failedJob!.LeaseToken,json,new("fake","fake"),default);
  db.ChangeTracker.Clear();
  var savedScan = await db.SecurityScanRows.SingleAsync(x => x.Id == failedId);
  if(savedScan.Status!="Completed" || string.IsNullOrEmpty(savedScan.Error)
      || !await db.SecurityReportRows.AnyAsync(x=>x.ScanId==failedId) || await db.ProjectIssues.CountAsync()!=4)
      throw new Exception("Failed auto assignment must preserve the scan report and show a warning");
  employee=await db.Employees.SingleAsync();employee.Status="ACTIVE";await db.SaveChangesAsync();
  var rejectedIssue=await db.ProjectIssues.OrderBy(x=>x.IssueId).FirstAsync();
  rejectedIssue.IssueStatus="REJECT";
  await SecurityIssueWorkflow.StageStatusAsync(db,rejectedIssue,"AcceptedRisk",1);await db.SaveChangesAsync();
  var acceptedLink=await db.SecurityFindingIssueLinkRows.FirstAsync(x=>x.IssueId==rejectedIssue.IssueId);
  var acceptedId=acceptedLink.FindingId;
  async Task CheckAccepted(long versionId,int expectedIssues=4){
   var scanId=await service.QueueAsync(versionId,"Passive",Guid.NewGuid().ToString(),1,default);
   var claimed=await service.ClaimAsync(default);
   await service.CompleteAsync(scanId,claimed!.LeaseToken,json,new("fake","accepted-risk regression"),default);
   db.ChangeTracker.Clear();
   if(!await db.SecurityScanFindingRows.AnyAsync(x=>x.ScanId==scanId&&x.FindingId==acceptedId)
      ||!await db.SecurityFindingRows.AnyAsync(x=>x.Id==acceptedId&&x.ReviewStatus=="AcceptedRisk")
      ||!await db.ProjectIssues.AnyAsync(x=>x.IssueId==rejectedIssue.IssueId&&x.IssueStatus=="REJECT")
      ||await db.ProjectIssues.CountAsync()!=expectedIssues)throw new Exception("Repeated finding must retain accepted risk and its rejected Issue without duplication");
  }
  await CheckAccepted(version.Id);
  var nextVersion=new SecurityVersion{SystemId=system.Id,VersionName="2",Environment="UAT"};db.SecurityVersionRows.Add(nextVersion);await db.SaveChangesAsync();
  await CheckAccepted(nextVersion.Id);
  Console.WriteLine("PASS REJECT / AcceptedRisk survives rescans of the same and next version without reopening or duplicating Issues");
  var passed=await db.ProjectIssues.Where(x=>x.IssueStatus!="REJECT").OrderBy(x=>x.IssueId).FirstAsync();
  passed.IssueStatus="PASS";passed.DevStatus="FIXED";
  await SecurityIssueWorkflow.StageStatusAsync(db,passed,"VerifiedFixed",1);await db.SaveChangesAsync();
  await CheckAccepted(nextVersion.Id,5);
  var unchanged=await db.ProjectIssues.SingleAsync(x=>x.IssueId==passed.IssueId);
  if(unchanged.IssueStatus!="PASS"||unchanged.DevStatus!="FIXED")throw new Exception("Original PASS Issue must remain unchanged");
  var recurring=await db.ProjectIssues.OrderByDescending(x=>x.IssueId).FirstAsync();
  if(recurring.IssueStatus!="OPEN"||recurring.DevStatus!="WIP"||!recurring.IssueDetail!.Contains($"Issue #{passed.IssueId}"))throw new Exception("Recurring finding must create an OPEN Issue referencing the original");
  var recurringLink=await db.SecurityFindingIssueLinkRows.FirstAsync(x=>x.IssueId==recurring.IssueId);
  if(!await db.SecurityFindingIssueLinkRows.AnyAsync(x=>x.FindingId==recurringLink.FindingId&&x.IssueId==passed.IssueId))throw new Exception("Original Issue link must be preserved");
  if(!await db.SecurityFindingRows.AnyAsync(x=>x.Id==recurringLink.FindingId&&x.ReviewStatus=="InProgress"))throw new Exception("Recurring finding must be InProgress");
  await SecurityIssueWorkflow.StageStatusAsync(db,unchanged,"VerifiedFixed",1);await db.SaveChangesAsync();
  if((await db.SecurityFindingRows.SingleAsync(x=>x.Id==recurringLink.FindingId)).ReviewStatus!="InProgress")throw new Exception("Old Issue must not overwrite the current recurrence status");
  await CheckAccepted(nextVersion.Id,5);
  Console.WriteLine("PASS recurring PASS finding creates one new Issue, keeps original Issue/link and ignores stale status updates; REJECT persists");
  var queuedForDelete=await service.QueueAsync(nextVersion.Id,"Passive",Guid.NewGuid().ToString(),1,default);
  try{await service.DeleteVersionAsync(nextVersion.Id,default);throw new Exception("Queued version deleted");}catch(InvalidOperationException){}
  if(!await db.SecurityVersionRows.AnyAsync(x=>x.Id==nextVersion.Id))throw new Exception("Blocked deletion must preserve version");
  await db.SecurityScanRows.Where(x=>x.Id==queuedForDelete).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.Status,"Cancelled"));
  var countBeforeDelete=await db.ProjectIssues.CountAsync();
  var filesForCleanup=await service.DeleteVersionAsync(nextVersion.Id,default);
  if(await db.SecurityVersionRows.AnyAsync(x=>x.Id==nextVersion.Id)||await db.SecurityScanRows.AnyAsync(x=>x.VersionId==nextVersion.Id)||await db.ProjectIssues.CountAsync()!=countBeforeDelete||!await db.SecurityVersionRows.AnyAsync(x=>x.Id==version.Id)||filesForCleanup.Count==0)throw new Exception("Version deletion must remove its history/reports while preserving Issues and other versions");
  Console.WriteLine("PASS version deletion blocks pending scans, deletes terminal history and preserves Issues/other versions");
  Console.WriteLine("PASS automatic Issues: Critical/High/Medium/Low, excluded Info, copied assignee/dates, atomic completion retry and finding deduplication");
 }
}
