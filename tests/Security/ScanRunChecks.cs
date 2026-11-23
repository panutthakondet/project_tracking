using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;
using ProjectTracking.ViewModels.Security;
public static class ScanRunChecks
{
 public static async Task Run(){
  using var connection=new SqliteConnection("DataSource=:memory:");await connection.OpenAsync();
  await using var db=new TestDb(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
  var system=new SecuritySystem{ProjectId=1,Name="Website"};db.SecuritySystemRows.Add(system);await db.SaveChangesAsync();
  var version=new SecurityVersion{SystemId=system.Id,VersionName="1"};db.SecurityVersionRows.Add(version);await db.SaveChangesAsync();
  var jobs=Enumerable.Range(1,3).Select(i=>new SecurityScan{VersionId=version.Id,Status=i==1?"Completed":i==2?"Failed":"Queued",TargetUrl="https://example.com/"+i,Error=i==2?"fixture login failed":null}).ToArray();
  db.SecurityScanRows.AddRange(jobs);await db.SaveChangesAsync();
  for(var i=0;i<3;i++)db.SecurityWebsiteScanParts.Add(new(){ScanId=jobs[i].Id,BatchKey="same-click",Section=SecurityValues.WebsiteSections[i]});
  // A different click and a different workflow are never merged by timestamp or URL.
  var next=new SecurityScan{VersionId=version.Id,Status="Completed"};var dev=new SecurityScan{VersionId=version.Id,Workflow="Development",Status="Completed"};
  db.SecurityScanRows.AddRange(next,dev);await db.SaveChangesAsync();
  db.SecurityWebsiteScanParts.Add(new(){ScanId=next.Id,BatchKey="other-click",Section="WebInfo"});
  db.SecurityWebsiteScanParts.Add(new(){ScanId=dev.Id,BatchKey="dev-click",Section="WebInfo"});await db.SaveChangesAsync();
  var run=await SecurityScanRun.LoadAsync(db,jobs[1],default);
  Check(run.Scans.Count==3&&run.Scan.Id==jobs[0].Id&&run.HasResults&&!run.Terminal&&run.Scan.Status=="Queued","all sibling IDs resolve to same run, excluding other workflows/clicks");
  var progress=ScanProgressPage.FromRun(run,Array.Empty<SecurityScanProgress>());
  Check(progress.IsBatch&&progress.Percent==66&&!progress.Terminal&&progress.Steps.Length==3,"combined progress counts finished parts, not successful coverage");
  Check(SecurityScanRun.Group(await db.SecurityScanRows.ToListAsync(),await db.SecurityWebsiteScanParts.ToListAsync()).Count==3,"history uses one row per click");
  jobs[2].Status="Completed";await db.SaveChangesAsync();run=await SecurityScanRun.LoadAsync(db,jobs[0],default);
  Check(run.Terminal&&run.Scan.Status=="Failed"&&run.HasResults&&run.Scan.Error!.Contains("fixture login failed"),"partial failure never becomes a successful run");
  var f=new SecurityFinding{SystemId=system.Id,Title="fixture",Fingerprint="fixture"};db.SecurityFindingRows.Add(f);await db.SaveChangesAsync();
  foreach(var job in new[]{jobs[0],jobs[2]})db.SecurityScanFindingRows.Add(new(){ScanId=job.Id,FindingId=f.Id,Title="fixture",Severity=job.Id==jobs[0].Id?"High":"Low"});await db.SaveChangesAsync();
  var ids=run.Ids;var observations=await db.SecurityScanFindingRows.Where(x=>ids.Contains(x.ScanId)).ToListAsync();
  var md=SecurityMarkdownReport.Create(run.Scan,version,system,"Project",observations,run.Parts.ToDictionary(x=>x.ScanId,x=>SecurityValues.SectionLabel(x.Section)));
  Check(observations.Count==2&&md.Contains("Web Info")&&md.Contains("Web Admin")&&md.Contains("fixture login failed")==false&&md.Contains("ไม่สำเร็จ"),"combined report includes observations from successful parts and marks failed overall status");
  Console.WriteLine("PASS Website runs group history/results/progress by click; preserve source sections and partial failures");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
