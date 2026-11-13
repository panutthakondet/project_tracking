using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;

static class PushDiagnosticChecks
{
    public static async Task Run(IDbContextFactory<AppDbContext> factory, WebNotificationService service)
    {
        await using var db=await factory.CreateDbContextAsync();
        await db.WebPushDeliveries.Where(d=>!d.Done).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Done,true));
        var subscription=await db.WebPushSubscriptions.AsNoTracking().FirstAsync(s=>s.Active && s.UserId==1);
        var key=await db.WebPushKeys.AsNoTracking().SingleAsync();
        var logger=new PushCheckLogger();var handler=new FakeHttp();
        using var services=new ServiceCollection().AddSingleton(factory).AddSingleton(service).BuildServiceProvider();
        var worker=new WebPushDispatcher(services.GetRequiredService<IServiceScopeFactory>(),new FakeHttpFactory(handler),logger);
        var dispatch=typeof(WebPushDispatcher).GetMethod("DispatchAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
        try
        {
            foreach(var stage in new[]{"DecryptSubscription","ReadVapidKey"})
            {
                await db.WebPushSubscriptions.Where(s=>s.Id==subscription.Id).ExecuteUpdateAsync(s=>s.SetProperty(s=>s.ProtectedSubscription,
                    stage=="DecryptSubscription"?"invalid-protected-subscription":subscription.ProtectedSubscription));
                await db.WebPushKeys.ExecuteUpdateAsync(s=>s.SetProperty(k=>k.ProtectedPrivateKey,
                    stage=="ReadVapidKey"?"invalid-protected-key":key.ProtectedPrivateKey));
                var notification=new WebNotification{UserId=1,EmpId=10,Title=stage,Message="test",TargetUrl="/",DedupKey=Guid.NewGuid().ToString("N"),CreatedAt=DateTime.UtcNow};
                db.WebNotifications.Add(notification);await db.SaveChangesAsync();
                var job=new WebPushDelivery{NotificationId=notification.Id,SubscriptionId=subscription.Id,NextAttemptAt=DateTime.UtcNow};
                db.WebPushDeliveries.Add(job);await db.SaveChangesAsync();
                await (Task)dispatch.Invoke(worker,[CancellationToken.None])!;
                if(!logger.Messages.Any(m=>m.Contains("stage "+stage)&&m.Contains("CryptographicException")))throw new Exception("Missing crypto stage diagnostic "+stage);
                await db.WebPushDeliveries.Where(d=>d.Id==job.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Done,true));
            }
            if(handler.Calls!=0||logger.Messages.Any(m=>m.Contains("fcm.googleapis.com")||m.Contains(subscription.ProtectedSubscription)||m.Contains(key.ProtectedPrivateKey)))
                throw new Exception("Crypto diagnostic leaked subscription/key or sent externally");
        }
        finally
        {
            await db.WebPushSubscriptions.Where(s=>s.Id==subscription.Id).ExecuteUpdateAsync(s=>s.SetProperty(s=>s.ProtectedSubscription,subscription.ProtectedSubscription));
            await db.WebPushKeys.ExecuteUpdateAsync(s=>s.SetProperty(k=>k.ProtectedPrivateKey,key.ProtectedPrivateKey));
        }
        Console.WriteLine("PASS dispatcher identifies subscription vs VAPID cryptographic failure without logging stored secrets or sending externally");
    }
}
class PushCheckLogger:ILogger<WebPushDispatcher>
{
    public List<string> Messages {get;}=[];
    public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
    public bool IsEnabled(LogLevel level)=>true;
    public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,exception));
}
