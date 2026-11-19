using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;
using ProjectTracking.ViewModels.Security;
using ProjectTracking.SecurityWorker;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

public static class ProgressChecks
{
    public static async Task Run(AppDbContext db,SecurityScanService service,long unusedVersionId)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        var system=new SecuritySystem{ProjectId=1,Name="Mobile progress fixture",Kind="Android"};db.SecuritySystemRows.Add(system);await db.SaveChangesAsync();
        var version=new SecurityVersion{SystemId=system.Id,VersionName="progress",Environment="UAT"};db.SecurityVersionRows.Add(version);await db.SaveChangesAsync();
        db.SecurityArtifactRows.Add(new(){VersionId=version.Id,OriginalName="fixture.apk",StorageKey="fixture.apk",Sha256=new string('0',64),Size=1000});await db.SaveChangesAsync();
        var id=await service.QueueAsync(version.Id,"Static",Guid.NewGuid().ToString(),1,default);var job=(await service.ClaimAsync(default))!;
        Check(job.Id==id,"progress fixture claims its own scan");
        Check(!await service.HeartbeatAsync(id,"wrong",default,new("Downloading",50,500,1000))&&!await db.SecurityScanProgressRows.AnyAsync(x=>x.ScanId==id),"wrong lease cannot insert or overwrite progress");
        Check(await service.HeartbeatAsync(id,job.LeaseToken,default,new("Downloading",43,430,1000)),"active worker reports actual transfer progress");
        var row=await db.SecurityScanProgressRows.AsNoTracking().SingleAsync(x=>x.ScanId==id);
        Check(row.Percent==43&&row.BytesDone==430&&row.BytesTotal==1000,"transfer byte counts persist independently of reports");
        await service.HeartbeatAsync(id,job.LeaseToken,default,new("Analyzing"));
        await service.HeartbeatAsync(id,job.LeaseToken,default,new("Downloading",10,100,1000));
        row=await db.SecurityScanProgressRows.AsNoTracking().SingleAsync(x=>x.ScanId==id);
        Check(row.Stage=="Analyzing"&&row.Percent==null,"late progress does not regress the stage or invent analysis percentages");
        Check(await service.HeartbeatAsync(id,job.LeaseToken,default),"older workers can still renew a lease without progress");
        Check(!SecurityProgress.IsValid(new("Downloading",101))&&!SecurityProgress.IsValid(new("Uploading",50,200,100))&&!SecurityProgress.IsValid(new("unknown")),"invalid percentages, bytes and stages rejected");
        await service.CompleteAsync(id,job.LeaseToken,"{\"code_analysis\":{\"findings\":{}}}",new("fixture","fixture"),default);
        Check(!await service.HeartbeatAsync(id,job.LeaseToken,default,new("Uploading",10)),"completed jobs cannot overwrite progress");
        var scan=await db.SecurityScanRows.AsNoTracking().SingleAsync(x=>x.Id==id);var done=ScanProgressPage.From(scan,row);
        Check(done.Percent==100&&done.Terminal&&done.StepIndex==done.Steps.Length,"100 percent overall is shown only after report completion");
        var active=new SecurityScan{Id=999,Engine="MobSF",Status="Running",LeaseToken="fixture-private-lease"};
        var analysis=ScanProgressPage.From(active,new(){ScanId=999,Stage="Analyzing",Percent=90});
        Check(analysis.Percent==null&&!analysis.Terminal,"analysis view stays indeterminate even if a worker supplies a percentage");
        Check(ScanProgressPage.From(active,null).Percent==null,"old in-flight workers have an honest indeterminate fallback");
        Check(!JsonSerializer.Serialize(analysis).Contains("fixture-private-lease"),"public progress response never exposes lease tokens");
        var cancelledId=await service.QueueAsync(version.Id,"Static",Guid.NewGuid().ToString(),1,default);var cancelled=(await service.ClaimAsync(default))!;
        await db.SecurityScanRows.Where(x=>x.Id==cancelledId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Cancelled"));
        Check(!await service.HeartbeatAsync(cancelledId,cancelled.LeaseToken,default,new("Analyzing")),"cancelled jobs cannot publish progress");

        var file=Path.GetTempFileName();try{
            var bytes=Enumerable.Range(0,200000).Select(i=>(byte)(i%251)).ToArray();await File.WriteAllBytesAsync(file,bytes);
            var updates=new List<ScanProgressUpdate>();using var content=ScanUploadContent.Create(file,"application/vnd.android.package-archive",updates.Add);
            var sent=await content.ReadAsByteArrayAsync();
            Check(sent.SequenceEqual(bytes)&&updates.Last().Percent==100&&updates.Last().BytesDone==bytes.Length&&updates.All(p=>p.BytesTotal==bytes.Length),"upload progress measures serialized file bytes without modifying APK contents");
        }finally{File.Delete(file);}
        using var handler=new HeartbeatHandler();using var api=new HttpClient(handler){BaseAddress=new Uri("http://fixture/")};
        using var work=new CancellationTokenSource();using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(200));using var reporter=new ScanProgressReporter();
        reporter.Update(new("Analyzing"));await reporter.RunAsync(api,job,work,stop.Token);
        Check(handler.SawProgress&&!work.IsCancellationRequested,"progress shares the authenticated lease heartbeat and stops cleanly");
    }
    private sealed class HeartbeatHandler:HttpMessageHandler
    {
        public bool SawProgress{get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            using var data=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            SawProgress=data.RootElement.GetProperty("progress").GetProperty("stage").GetString()=="Analyzing"&&data.RootElement.TryGetProperty("leaseToken",out _);
            return new(HttpStatusCode.NoContent);
        }
    }
}
