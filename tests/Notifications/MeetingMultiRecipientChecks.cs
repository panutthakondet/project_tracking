using Microsoft.AspNetCore.DataProtection;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;
using WebPush;

static class MeetingMultiRecipientChecks
{
    public static async Task Run(IDbContextFactory<AppDbContext> factory, WebNotificationService web, TelegramMessagingService telegram)
    {
        await using var db=await factory.CreateDbContextAsync();
        // Keep this dispatch check isolated from jobs produced by earlier tests.
        await db.WebPushDeliveries.Where(d=>!d.Done).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Done,true));
        db.Employees.AddRange(Enumerable.Range(100,6).Select(id=>new Employee{EmpId=id,EmpName="multi-"+id,DepartmentId=1,PositionId=7}));
        db.LoginUsers.AddRange(new LoginUser{UserId=100,EmpId=100,Username="multi-one",Password="test",Status="ACTIVE"},
            new LoginUser{UserId=101,EmpId=101,Username="multi-two",Password="test",Status="ACTIVE"},
            new LoginUser{UserId=102,EmpId=102,Username="multi-inactive",Password="test",Status="INACTIVE"},
            new LoginUser{UserId=103,EmpId=103,Username="multi-no-device",Password="test",Status="ACTIVE"},
            new LoginUser{UserId=104,EmpId=104,Username="multi-outsider",Password="test",Status="ACTIVE"});
        db.Meetings.Add(new Meeting{Id=100,Title="multi attendee regression",MeetingDate=DateTime.UtcNow.AddHours(7).Date,StartTime=TimeSpan.FromHours(9),EndTime=TimeSpan.FromHours(10)});
        // Include a duplicate attendee plus one inactive and one unlinked employee.
        db.MeetingAttendees.AddRange(new[]{100,100,101,102,103,105}.Select(id=>new MeetingAttendee{MeetingId=100,UserId=id}));
        await db.SaveChangesAsync();
        var keys=VapidHelper.GenerateVapidKeys();
        WebPushSubscription Device(int user,string suffix,bool corrupt=false)=>new(){UserId=user,EndpointHash=WebNotificationService.Hash(suffix),SessionHash="test",Active=true,UpdatedAt=DateTime.UtcNow,
            ProtectedSubscription=corrupt?"invalid-protected-subscription":web.Protector.Protect(JsonSerializer.Serialize(new{endpoint="https://fcm.googleapis.com/fcm/send/"+suffix,keys=new{p256dh=keys.PublicKey,auth=Convert.ToBase64String(new byte[16]).TrimEnd('=').Replace('+','-').Replace('/','_')}}))};
        var bad=Device(100,"multi-bad",true);var good=Device(100,"multi-good");var other=Device(101,"multi-other");
        db.WebPushSubscriptions.AddRange(bad,good,other,Device(104,"multi-outsider"));await db.SaveChangesAsync();
        var coordinator=new MeetingNotificationService(factory,null!,null!,null!,telegram,null!,new ConfigurationBuilder().Build(),NullLogger<MeetingNotificationService>.Instance,web);
        await coordinator.SendCreatedNotificationsAsync(100);
        var notifications=await db.WebNotifications.AsNoTracking().Where(n=>n.TargetUrl=="/Meetings/Show/100").ToListAsync();
        if(!notifications.Select(n=>n.UserId).Order().SequenceEqual(new[]{100,101,103}))throw new Exception("Meeting multi-attendee fanout missing recipient, duplicated attendee or included inactive/unselected account");
        var ids=notifications.Select(n=>n.Id).ToArray();
        var jobs=await db.WebPushDeliveries.AsNoTracking().Where(d=>ids.Contains(d.NotificationId)).ToListAsync();
        if(jobs.Count!=3||!jobs.Select(d=>d.SubscriptionId).Order().SequenceEqual(new[]{bad.Id,good.Id,other.Id}.Order()))throw new Exception("Multi-attendee per-device push fanout failed");
        await coordinator.SendCreatedNotificationsAsync(100);
        if(await db.WebNotifications.CountAsync(n=>n.TargetUrl=="/Meetings/Show/100")!=3||await db.WebPushDeliveries.CountAsync(d=>ids.Contains(d.NotificationId))!=3)throw new Exception("Meeting retry duplicated multi-recipient notifications/jobs");
        var transport=new FakeHttp();
        using var services=new ServiceCollection().AddSingleton(factory).AddSingleton(web).BuildServiceProvider();
        var dispatcher=new WebPushDispatcher(services.GetRequiredService<IServiceScopeFactory>(),new FakeHttpFactory(transport),NullLogger<WebPushDispatcher>.Instance);
        await (Task)typeof(WebPushDispatcher).GetMethod("DispatchAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dispatcher,[CancellationToken.None])!;
        jobs=await db.WebPushDeliveries.AsNoTracking().Where(d=>ids.Contains(d.NotificationId)).ToListAsync();
        if(transport.Calls!=2||jobs.Single(d=>d.SubscriptionId==bad.Id).Done||jobs.Where(d=>d.SubscriptionId!=bad.Id).Any(d=>!d.Done))throw new Exception("One corrupt device blocked another attendee/device");
        Console.WriteLine("PASS actual meeting multi-attendee inbox + per-device push, duplicate/inactive/unlinked exclusion, retry dedup and independent device failure");
    }
}
