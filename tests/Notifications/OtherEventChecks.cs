using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;

static class OtherEventChecks
{
    public static async Task Run(IDbContextFactory<AppDbContext> factory,WebNotificationService notifications,TelegramMessagingService telegram,IConfiguration config)
    {
        await using var db=await factory.CreateDbContextAsync();
        db.CntMCoops.Add(new CntMCoop{CoopId=100,CoopName="สหกรณ์ทดสอบ"});
        db.FieldServiceVisits.Add(new FieldServiceVisit{VisitId=50,CoopId=100,Title="งานเข้าไซต์ทดสอบ",VisitDate=DateTime.Today,
            Assignees=[new FieldServiceAssignee{EmpId=10},new FieldServiceAssignee{EmpId=20}]});
        await db.SaveChangesAsync();
        var field=new FieldServiceNotificationService(factory,null!,null!,null!,notifications,telegram,null!,config,NullLogger<FieldServiceNotificationService>.Instance);
        var task=(Task)typeof(FieldServiceNotificationService).GetMethod("LoadVisitAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(field,[50,CancellationToken.None])!;await task;
        var details=task.GetType().GetProperty("Result")!.GetValue(task)!;
        var recipients=((IEnumerable)details.GetType().GetProperty("Recipients")!.GetValue(details)!).Cast<object>().ToArray();
        static object? Value(object row,string property)=>row.GetType().GetProperty(property)!.GetValue(row);
        if(recipients.Length!=2||!((string)Value(recipients[0],"DisplayName")!).Contains("ผู้จัดการโครงการ")||(string)Value(recipients[1],"DisplayName")! !="พนักงานสอง"||!recipients.Any(r=>(string?)Value(r,"Email")=="preferred@example.test"))throw new Exception("Field service current-position/fallback recipient load failed: "+string.Join(" | ",recipients.Select(r=>r.ToString())));
        Console.WriteLine("PASS FieldService positions use current master data without dropping recipients with absent definitions");
        var before=await db.WebNotifications.CountAsync(n=>n.UserId==1);
        var linkedBefore=await db.WebNotifications.CountAsync(n=>n.UserId==4);
        var jobs=await db.WebPushDeliveries.CountAsync();
        var events=new (string Title,string Url)[]{
            ("แจ้ง Assign ใหม่: audit","/PhaseAssigns/Index?projectId=4&empId=10"),
            ("แจ้ง Issue ใหม่: audit","/ProjectIssues/DevDetails/4"),
            ("แจ้ง Issue FIXED: audit","/ProjectIssues/Details/4"),
            ("แจ้ง BA result: audit","/ProjectIssues/DevDetails/4"),
            ("แจ้ง Support ใหม่: audit","/SupportOrders/Details/4"),
            ("แจ้ง Support FIXED: audit","/SupportOrders/Details/4"),
            ("แจ้ง Support BA result: audit","/SupportOrdersDev/Details/4"),
            ("Followup ACK: audit","/Followups/Details/4"),
            ("แจ้งเตือนงานเข้าไซต์: audit","/FieldService/Show/50"),
            ("งานเสี่ยง/ล่าช้า: audit","/PhaseAssigns/Index?projectId=4")};
        foreach(var item in events)await notifications.NotifyEmployeeSafelyAsync(10,item.Title,"ผู้รับผิดชอบทดสอบ 📝",item.Url);
        if(await db.WebNotifications.CountAsync(n=>n.UserId==1)!=before+events.Length||await db.WebPushDeliveries.CountAsync()!=jobs+events.Length)throw new Exception("Other event shared notification delivery failed");
        if(await db.WebNotifications.CountAsync(n=>n.UserId==4)!=linkedBefore+events.Length)throw new Exception("Other events did not reach employee-side linked login account");
        if(await db.WebNotifications.AnyAsync(n=>n.UserId==3))throw new Exception("Inactive login received other-screen notification");
        Console.WriteLine("PASS all audited event payloads also reach employee-side linked accounts and exclude inactive accounts");
        Console.WriteLine("PASS assignment, issue/fix/BA result, support, followup, site visit and overdue payloads reach owned inbox/push outbox without Telegram linkage");
    }
}
