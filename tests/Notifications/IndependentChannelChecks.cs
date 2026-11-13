using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTracking.Controllers;
using ProjectTracking.Data;
using ProjectTracking.Services;

static class IndependentChannelChecks
{
    public static async Task Run(IDbContextFactory<AppDbContext> factory, WebNotificationService web,
        TelegramMessagingService telegram, IConfiguration config)
    {
        await using var db = await factory.CreateDbContextAsync();
        foreach (var type in new[]{typeof(PhaseAssignsController),typeof(ProjectIssuesController),typeof(SupportOrdersController),
            typeof(SupportOrdersDevController),typeof(FollowupsController),typeof(EmployeesController)})
        {
            var constructor=type.GetConstructors().Single();
            var args=constructor.GetParameters().Select(p=>p.ParameterType==typeof(AppDbContext)?(object)db:
                p.ParameterType==typeof(WebNotificationService)?web:p.ParameterType==typeof(TelegramMessagingService)?telegram:
                p.ParameterType==typeof(IConfiguration)?config:null).ToArray();
            var controller=(Controller)constructor.Invoke(args);
            controller.ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()};
            var method=type.GetMethod("SendChatNotificationToEmployeeSafelyAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var title="independent controller: "+type.Name;
            var values=method.GetParameters().Select(p=>p.ParameterType==typeof(int)?(object)10:
                p.ParameterType==typeof(bool)?false:p.Name=="title"?title:p.Name=="targetUrl"?"/":"test").ToArray();
            await (Task)method.Invoke(controller,values)!;
            if(!await db.WebNotifications.AnyAsync(n=>n.UserId==1 && n.Title==title))
                throw new Exception(type.Name+" failed web delivery with both chat channels disabled");
        }
        Console.WriteLine("PASS all six controller dispatchers insert owned inbox with LINE/Telegram disabled and no chat services");
        var before=await db.WebNotifications.CountAsync();
        await telegram.SendNotificationToEmployeeAsync(10,"Telegram transport only","test","/");
        await telegram.SendNotificationToChatIdsAsync([],"Telegram chat only","test","/",empIdForLog:10);
        if(await db.WebNotifications.CountAsync()!=before)throw new Exception("Telegram transport still writes web notifications");
        Console.WriteLine("PASS employee and direct-chat Telegram transports never mirror inbox");

        var line=new LineMessagingService(new HttpClient(),factory,config,NullLogger<LineMessagingService>.Instance);
        var field=new FieldServiceNotificationService(factory,null!,line,null!,web,telegram,null!,config,NullLogger<FieldServiceNotificationService>.Instance);
        await field.SendCreatedNotificationsAsync(50);
        if(!await db.WebNotifications.AnyAsync(n=>n.UserId==1 && n.Title.StartsWith("มอบหมายงานเข้าไซต์:")))
            throw new Exception("FieldService event blocked by unavailable external channels");
        var reminders=await field.SendWebRemindersAsync();
        if(reminders.SentCount==0)throw new Exception("Independent site-visit reminder failed");
        if((await field.SendWebRemindersAsync()).SentCount!=0)throw new Exception("Site-visit web reminder daily dedup failed");
        var meetings=new MeetingNotificationService(factory,null!,null!,null!,telegram,null!,config,NullLogger<MeetingNotificationService>.Instance,web);
        if((await meetings.SendWebRemindersAsync()).SentCount==0)throw new Exception("Independent meeting reminder failed");
        if((await meetings.SendWebRemindersAsync()).SentCount!=0)throw new Exception("Meeting web reminder daily dedup failed");
        foreach(var operation in new[]{"SendUpdatedNotificationsAsync","SendCancelledNotificationsAsync"})
        {
            // Legacy external-channel SQL is MySQL-only in this isolated SQLite test.
            try { await (Task)typeof(MeetingNotificationService).GetMethod(operation)!.Invoke(meetings,[70,CancellationToken.None])!; }
            catch (Microsoft.Data.Sqlite.SqliteException) { }
        }
        if(!await db.WebNotifications.AnyAsync(n=>n.UserId==1 && n.Title.StartsWith("อัปเดตประชุม:")) ||
           !await db.WebNotifications.AnyAsync(n=>n.UserId==1 && n.Title.StartsWith("ยกเลิกประชุม:")))
            throw new Exception("Meeting update/cancel web events blocked by external channel SQL");
        var overdue=new OverdueNotificationService(factory,line,null!,web,telegram,null!,config,NullLogger<OverdueNotificationService>.Instance);
        var payloadType=typeof(OverdueNotificationService).GetNestedType("TelegramNotificationPayload",BindingFlags.NonPublic)!;
        var queue=Array.CreateInstance(payloadType,1);
        queue.SetValue(Activator.CreateInstance(payloadType,[10,"งานเกินกำหนดอิสระ","รายละเอียด","/PhaseAssigns/Index"]),0);
        var dispatch=typeof(OverdueNotificationService).GetMethod("SendTelegramNotificationsAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
        await (Task)dispatch.Invoke(overdue,[queue,CancellationToken.None])!;
        var overdueCount=await db.WebNotifications.CountAsync(n=>n.UserId==1 && n.Title=="งานเกินกำหนดอิสระ");
        if(overdueCount!=1)throw new Exception("Automatic overdue web blocked by disabled chats");
        await (Task)dispatch.Invoke(overdue,[queue,CancellationToken.None])!;
        if(await db.WebNotifications.CountAsync(n=>n.UserId==1 && n.Title=="งานเกินกำหนดอิสระ")!=overdueCount)
            throw new Exception("Independent overdue daily dedup failed");
        Console.WriteLine("PASS meeting update/cancel web survives external SQL failure; automatic overdue web ignores disabled chats and deduplicates daily");
        Console.WriteLine("PASS actual FieldService event and meeting/site web reminders work without chat configuration and deduplicate daily");
    }
}
