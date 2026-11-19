using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;

public static class HostApprovalChecks
{
 public static async Task Run(AppDbContext db,SecurityScanService service)
 {
  void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
  Check(SecurityScanService.TargetHost("https://EXAMPLE.com./path?q=1")=="example.com","approval uses normalized host rather than paths, case or query");
  var system=new SecuritySystem{ProjectId=42,Name="Approval fixture",TargetUrl="https://93.184.216.34/path"};db.SecuritySystemRows.Add(system);await db.SaveChangesAsync();
  var version=new SecurityVersion{SystemId=system.Id,VersionName="approval-test",Environment="UAT"};db.SecurityVersionRows.Add(version);await db.SaveChangesAsync();
  async Task Rejected(){try{await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);throw new Exception("Unapproved queue accepted");}catch(InvalidOperationException e)when(e.Message.Contains("โดเมน")){} }
  await Rejected();
  await service.SetHostApprovalAsync(43,"93.184.216.34",true,2,default);await Rejected();
  Check(!await service.IsHostApprovedAsync(42,"93.184.216.34",default),"another project's approval cannot authorize a scan");
  await service.SetHostApprovalAsync(42,"93.184.216.34",true,2,default);
  var id=await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);var job=await service.ClaimAsync(default);
  Check(job?.Id==id&&job.ApprovedHosts?.SequenceEqual(new[]{"93.184.216.34"})==true,"claimed job carries only its exact approved target dynamically");
  await db.SecurityScanRows.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Cancelled"));
  var pending=await service.QueueAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);
  await service.SetHostApprovalAsync(42,"93.184.216.34",false,3,default);
  Check(await service.ClaimAsync(default)==null,"revocation prevents already queued work from being issued");
  Check(await db.SecurityScanRows.AsNoTracking().AnyAsync(x=>x.Id==pending&&x.Status=="Failed"),"revoked queued job records a controlled failure");
  await Rejected();
  var approval=await db.SecurityHostApprovals.AsNoTracking().SingleAsync(x=>x.ProjectId==42&&x.Host=="93.184.216.34");
  Check(!approval.IsApproved&&approval.UpdatedBy==3,"approval state records the last acting user and revocation");
 }
}
