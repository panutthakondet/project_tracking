using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;

static class MeetingChecks
{
    public static async Task Run(IDbContextFactory<AppDbContext> factory,WebNotificationService notifications,TelegramMessagingService telegram)
    {
        await using var db=await factory.CreateDbContextAsync();
        db.EmployeePositions.Add(new EmployeePosition{PositionId=7,PositionName="ผู้จัดการโครงการ"});
        db.Employees.AddRange(new Employee{EmpId=10,EmpName="พนักงานหนึ่ง",PositionId=7,DepartmentId=1,LoginUserId=4},new Employee{EmpId=20,EmpName="พนักงานสอง",PositionId=999,DepartmentId=1},new Employee{EmpId=30,EmpName="ไม่มีอีเมล",PositionId=7,DepartmentId=1});
        db.LoginUsers.Add(new LoginUser{UserId=4,Username="linked-mail",Password="test",Email="preferred@example.test",Status="ACTIVE"});
        await db.LoginUsers.Where(u=>u.UserId==1).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Email,"fallback@example.test"));
        await db.LoginUsers.Where(u=>u.UserId==2).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Email,"two@example.test"));
        db.MeetingAttendees.AddRange(new MeetingAttendee{Id=1,MeetingId=70,UserId=10},new MeetingAttendee{Id=2,MeetingId=70,UserId=20},new MeetingAttendee{Id=3,MeetingId=70,UserId=30});
        await db.SaveChangesAsync();
        async Task<object[]> Load(string method)
        {
            var operation=(Task)typeof(MeetingNotificationService).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[db,70,CancellationToken.None])!;
            await operation;return ((IEnumerable)operation.GetType().GetProperty("Result")!.GetValue(operation)!).Cast<object>().ToArray();
        }
        static object? Value(object row,string property)=>row.GetType().GetProperty(property)!.GetValue(row);
        var recipients=await Load("LoadMeetingRecipientsAsync");
        if(recipients.Length!=3||(int)Value(recipients[0],"EmpId")! !=10||!((string)Value(recipients[0],"DisplayName")!).Contains("ผู้จัดการโครงการ")||(string)Value(recipients[1],"DisplayName")! !="พนักงานสอง")throw new Exception("Meeting recipient position mapping failed");
        var email=await Load("LoadEmailRecipientsAsync");
        if(email.Length!=2||(string)Value(email[0],"Email")! !="preferred@example.test"||(string)Value(email[1],"Email")! !="two@example.test")throw new Exception("Meeting email lookup priority failed");
        var before=await db.WebNotifications.CountAsync();var jobs=await db.WebPushDeliveries.CountAsync();
        await telegram.SendNotificationToEmployeeAsync((int)Value(recipients[0],"EmpId")!,"เชิญประชุม: regression","ประชุมทดสอบหลังแก้โครงสร้างตำแหน่ง","/Meetings/Show/70");
        if(await db.WebNotifications.CountAsync()!=before+1||await db.WebPushDeliveries.CountAsync()!=jobs+1)throw new Exception("Meeting recipient inbox/push queue failed");
        Console.WriteLine("PASS meeting recipients use position_id/employee_positions, tolerate absent position, preserve email priority and queue inbox/push");
    }
}
