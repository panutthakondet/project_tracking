using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;

public static class WebsiteChecks
{
    public static async Task Run(string root)
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
        Check(SecurityValues.WebsiteUrl("https://uat.example.com/","/member/login")=="https://uat.example.com/member/login"&&SecurityValues.WebsiteUrl("https://uat.example.com/","/adminlogon")=="https://uat.example.com/adminlogon","one Website base URL resolves Member and Admin paths");
        foreach(var invalidPath in new[]{"//evil.example/","https://evil.example/","/\\evil","/path#fragment"}){
            try{SecurityValues.WebsiteUrl("https://uat.example.com/",invalidPath);throw new Exception("Unsafe path accepted");}catch(InvalidOperationException){}
        }
        Check(true,"Website paths cannot redirect credentials to an external origin");
        using var conn=new SqliteConnection("DataSource=:memory:");await conn.OpenAsync();
        await using var db=new TestDb(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);await db.Database.EnsureCreatedAsync();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["SECURITY_WORKER_KEY"]=new string('x',48),["SECURITY_STORAGE_PATH"]=Path.Combine(root,"website")}).Build();
        var storage=new SecurityStorage(config,new TestEnvironment(root));var store=new SecurityLoginStore(storage,new EphemeralDataProtectionProvider());
        var service=new SecurityScanService(db,storage,config,store);
        var system=new SecuritySystem{Kind="Website",ProjectId=7,Name="Website",TargetUrl="https://info.example.com/"};db.SecuritySystemRows.Add(system);await db.SaveChangesAsync();
        var version=new SecurityVersion{SystemId=system.Id,VersionName="website",Environment="UAT"};db.SecurityVersionRows.Add(version);await db.SaveChangesAsync();
        foreach(var (section,host) in new[]{("WebInfo","info"),("WebMember","member"),("WebAdmin","admin")}){
            var target=new SecurityWebsiteTarget{SystemId=system.Id,Section=section,Enabled=true,TargetUrl=$"https://{host}.example.com/",RequiresLogin=section!="WebInfo"};
            if(target.RequiresLogin)target.AuthProfile=await store.SaveAsync(system.Id,new(){Username=host,Password=host+"-secret",LoginUrl=target.TargetUrl+"login",LoggedInRegex="Sign out"},target.TargetUrl,default);
            db.SecurityWebsiteTargets.Add(target);await service.SetHostApprovalAsync(7,host+".example.com",true,1,default);
        }
        var key=Guid.NewGuid().ToString();var ids=await service.QueueWebsiteAsync(version.Id,"Passive",key,1,default);
        Check(ids.Count==3&&await db.SecurityWebsiteScanParts.CountAsync()==3,"one Website click queues three separate sections atomically");
        Check((await service.QueueWebsiteAsync(version.Id,"Passive",key,1,default)).Order().SequenceEqual(ids.Order())&&await db.SecurityScanRows.CountAsync()==3,"repeated Website request does not duplicate jobs");
        db.ChangeTracker.Clear();
        var parent=await db.SecuritySystemRows.AsNoTracking().SingleAsync();Check(parent.TargetUrl=="https://info.example.com/"&&!parent.RequiresLogin&&parent.AuthProfile==null,"section overrides never overwrite the parent Website settings");
        var member=await db.SecurityWebsiteTargets.SingleAsync(x=>x.Section=="WebMember");member.TargetUrl="https://changed.example.com/";member.AuthProfile=null;await db.SaveChangesAsync();
        var seen=new Dictionary<string,string>();
        for(var i=0;i<3;i++){
            var job=await service.ClaimAsync(default);Check(job!=null,"Website section can be claimed");
            var host=new Uri(job!.TargetUrl!).Host;seen[host]=job.Login?.Username??"public";
            if(host=="info.example.com")Check(!job.RequiresLogin&&job.Login==null,"Web Info remains unauthenticated");
            else Check(job.RequiresLogin&&job.Login?.Password==(host.StartsWith("admin")?"admin-secret":"member-secret"),"each authenticated section receives only its own credentials");
            await db.SecurityScanRows.Where(x=>x.Id==job.Id).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.Status,"Completed"));
        }
        Check(seen.Count==3&&seen["member.example.com"]=="member"&&seen["admin.example.com"]=="admin","queued target URL and encrypted Login snapshots survive later edits");
        db.ChangeTracker.Clear();var invalid=await db.SecurityWebsiteTargets.SingleAsync(x=>x.Section=="WebMember");invalid.Enabled=false;await db.SaveChangesAsync();
        var pair=await service.QueueWebsiteAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);Check(pair.Count==2,"disabled Website section is skipped");
        var before=await db.SecurityScanRows.CountAsync();await service.SetHostApprovalAsync(7,"info.example.com",false,1,default);
        try{await service.QueueWebsiteAsync(version.Id,"Passive",Guid.NewGuid().ToString(),1,default);throw new Exception("Unapproved Website accepted");}catch(InvalidOperationException){}
        db.ChangeTracker.Clear();Check(await db.SecurityScanRows.CountAsync()==before,"invalid section rolls back the entire Website batch without a partial queue");
        Check(SecurityValues.Kinds.SequenceEqual(new[]{"Website","Android","iOS"})&&SecurityValues.Category("WebAdmin")=="Website","three primary categories retain legacy website history");
    }
}
