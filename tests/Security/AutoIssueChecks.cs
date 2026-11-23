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
  var json=System.Text.Json.JsonSerializer.Serialize(new{alerts=new[]{"Critical","High","Medium","Low","Info"}.Select((severity,i)=>new{pluginId=i.ToString(),name="Finding "+severity,risk=severity,url="https://example.com/"+i,description="fixture",solution="fix"})});
  async Task Scan(){var id=await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var job=await service.ClaimAsync(default);await service.CompleteAsync(id,job!.LeaseToken,json,new("fake","fake"),default);await service.CompleteAsync(id,job.LeaseToken,json,new("fake","fake"),default);}
  await Scan();
  var issues=await db.ProjectIssues.ToListAsync();
  if(issues.Count!=4||issues.Any(x=>x.AssignTo!=employee.EmpId||x.StartDate!=start||x.EndDate!=end)||await db.SecurityFindingIssueLinkRows.CountAsync()!=4)throw new Exception("Automatic Issues must exclude Info and copy assignee/dates");
  await Scan();if(await db.ProjectIssues.CountAsync()!=4)throw new Exception("Repeated scan duplicated Issues");
  employee.Status="INACTIVE";await db.SaveChangesAsync();
  var failedId=await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var failedJob=await service.ClaimAsync(default);
  try{await service.CompleteAsync(failedId,failedJob!.LeaseToken,json,new("fake","fake"),default);throw new Exception("Inactive assignee was accepted");}catch(InvalidOperationException){}
  db.ChangeTracker.Clear();
  if(await db.SecurityReportRows.CountAsync()!=2||await db.ProjectIssues.CountAsync()!=4||await db.SecurityScanFindingRows.AnyAsync(x=>x.ScanId==failedId))throw new Exception("Failed auto assignment left partial results");
  Console.WriteLine("PASS automatic Issues: Critical/High/Medium/Low, excluded Info, copied assignee/dates, atomic completion retry and finding deduplication");
 }
}
