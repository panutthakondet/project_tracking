using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;

public static class LoginChecks
{
    public static async Task Run(string root)
    {
        var folder=Path.Combine(root,"login-fixture");
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["SECURITY_STORAGE_PATH"]=folder,["SECURITY_WORKER_KEY"]=new string('x',48)
        }).Build();
        var storage=new SecurityStorage(config,new TestEnvironment(root));
        var protection=new EphemeralDataProtectionProvider();
        var store=new SecurityLoginStore(storage,protection);
        var login=new SecurityLoginCredentials{LoginUrl="https://uat.example.com/login",Username="test-account",Password="fixture-secret",LoggedInRegex="ออกจากระบบ"};
        var first=await store.SaveAsync(1,login,"https://uat.example.com/",default);
        var bytes=await File.ReadAllTextAsync(storage.PathFor(first[4..]+".login"));
        Check(!bytes.Contains(login.Password)&&!bytes.Contains(login.Username),"login credentials are encrypted at rest");
        var reopened=await new SecurityLoginStore(storage,protection).ReadAsync(1,first,"https://uat.example.com/",default);
        Check(reopened.Password==login.Password&&reopened.LoggedInRegex==login.LoggedInRegex,"encrypted credentials round-trip with the same key ring");
        await Reject(()=>store.ReadAsync(2,first,"https://uat.example.com/",default),"another system cannot decrypt a copied profile reference");
        await Reject(()=>store.ReadAsync(1,first,"https://other.example.com/",default),"credentials cannot be sent to a different origin");
        await Reject(()=>store.ReadAsync(1,"../profile","https://uat.example.com/",default),"profile paths reject traversal");
        await Reject(()=>new SecurityLoginStore(storage,new EphemeralDataProtectionProvider()).ReadAsync(1,first,"https://uat.example.com/",default),"lost keys fail closed without credential disclosure");

        using var connection=new SqliteConnection("DataSource=:memory:");await connection.OpenAsync();
        await using var db=new TestDb(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.SecuritySystemRows.Add(new SecuritySystem{Id=1,ProjectId=1,Name="Private web",TargetUrl="https://uat.example.com/",RequiresLogin=true,AuthProfile=first});
        db.SecurityVersionRows.Add(new SecurityVersion{Id=1,SystemId=1,VersionName="1",Environment="UAT"});await db.SaveChangesAsync();
        var service=new SecurityScanService(db,storage,config,store);
        await service.SetHostApprovalAsync(1,"uat.example.com",true,1,default);
        await service.QueueAsync(1,"Passive",Guid.NewGuid().ToString(),1,default);
        login.Password="replacement-secret";
        var second=await store.SaveAsync(1,login,"https://uat.example.com/",default);
        (await db.SecuritySystemRows.SingleAsync()).AuthProfile=second;await db.SaveChangesAsync();
        var job=await service.ClaimAsync(default);
        Check(job?.Login?.Password=="fixture-secret"&&job.AuthProfile==first,"queued scan keeps its original encrypted login snapshot after editing");
        await service.QueueAsync(1,"Passive",Guid.NewGuid().ToString(),1,default);
        var next=await service.ClaimAsync(default);
        Check(next?.Login?.Password=="replacement-secret","new scans receive the updated credentials");
        Check(!next!.ToString().Contains("replacement-secret"),"job string representation does not expose the password");
    }
    private static async Task Reject(Func<Task> action,string message)
    {
        try { await action(); } catch(InvalidOperationException error) {
            Check(!error.Message.Contains("fixture-secret"),message);return;
        }
        throw new Exception(message);
    }
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message); }
}
