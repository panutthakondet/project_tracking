using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTracking.Controllers;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;
using WebPush;

Environment.SetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN","");Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN","");Environment.SetEnvironmentVariable("APP_BASE_URL","https://app.test");
void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
using var connection=new SqliteConnection("DataSource=:memory:");await connection.OpenAsync();
var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(new MysqlSqliteAdapter()).Options;
var factory=new Factory(options);await using(var setup=await factory.CreateDbContextAsync()){
 await setup.Database.EnsureCreatedAsync();setup.LoginUsers.AddRange(new LoginUser{UserId=1,EmpId=10,Username="one",Password="test",Status="ACTIVE"},new LoginUser{UserId=2,EmpId=20,Username="two",Password="test",Status="ACTIVE"},new LoginUser{UserId=3,EmpId=10,Username="inactive",Password="test",Status="INACTIVE"});await setup.SaveChangesAsync();}
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"WEB_PUSH_SUBJECT","mailto:test@example.test"}}).Build();var service=new WebNotificationService(factory,new EphemeralDataProtectionProvider(),config,NullLogger<WebNotificationService>.Instance);
var key1=await service.KeysAsync();var key2=await service.KeysAsync();Check(key1.PublicKey==key2.PublicKey&&key1.PrivateKey==key2.PrivateKey,"persistent VAPID key round trip through protected database storage");
Check(WebNotificationService.AllowedEndpoint("https://fcm.googleapis.com/fcm/send/test")&&!WebNotificationService.AllowedEndpoint("https://fcm.googleapis.com.evil.test/test")&&!WebNotificationService.AllowedEndpoint("https://127.0.0.1/internal")&&!WebNotificationService.AllowedEndpoint("http://web.push.apple.com/x"),"push provider allowlist blocks SSRF and HTTP endpoints");
Check(service.LocalTarget("https://app.test/Projects/Show/4?x=1")=="/Projects/Show/4?x=1"&&service.LocalTarget("https://evil.test/x")=="/"&&service.LocalTarget("//evil.test")=="/"&&service.LocalTarget("/\\evil.test")=="/","safe local links independent of VAPID mailto subject");
await using var db=await factory.CreateDbContextAsync();
WorkNotificationsController Controller(int? user){var context=new DefaultHttpContext();var session=new FakeSession();if(user.HasValue)session.SetInt32("UserId",user.Value);context.Features.Set<ISessionFeature>(new FakeSessionFeature{Session=session});return new WorkNotificationsController(db,service){ControllerContext=new ControllerContext{HttpContext=context},Url=new FakeUrl()};}
var one=Controller(1);var two=Controller(2);var anon=Controller(null);var inactive=Controller(3);
Check(await anon.List(null,default) is UnauthorizedResult&&await inactive.List(null,default) is UnauthorizedResult,"anonymous and inactive accounts cannot read notifications");
var subscriberKeys=VapidHelper.GenerateVapidKeys();var request=new WorkNotificationsController.SubscriptionRequest{Endpoint="https://fcm.googleapis.com/fcm/send/test-device",Keys=new(){P256dh=subscriberKeys.PublicKey,Auth=Convert.ToBase64String(new byte[16]).TrimEnd('=').Replace('+','-').Replace('/','_')}};
Check(await one.Subscribe(request,default) is OkResult,"authenticated browser subscription saved");
var subscription=await db.WebPushSubscriptions.AsNoTracking().SingleAsync();Check(!subscription.ProtectedSubscription.Contains("fcm.googleapis.com"),"subscription endpoint and auth material encrypted at rest");
var handler=new FakeHttp();var telegram=new TelegramMessagingService(new HttpClient(handler),factory,config,NullLogger<TelegramMessagingService>.Instance);
var telegramCount=await telegram.SendNotificationToEmployeeAsync(10,"transport only","test","/");Check(telegramCount==0&&!await db.WebNotifications.AnyAsync(),"unconfigured Telegram never inserts inbox or reports web delivery");
var sent=await service.NotifyEmployeeSafelyAsync(10,"งานใหม่ 📝","รายละเอียดทดสอบ","/Projects/Show/4");Check(sent==1&&handler.Calls==0,"independent web event reaches inbox without bot token or Telegram linkage");
Check(await service.NotifyEmployeeSafelyAsync(10,"งานใหม่ 📝","รายละเอียดทดสอบ","/Projects/Show/4")==0,"duplicate event retry creates only one inbox item and push job");
var first=await db.WebNotifications.AsNoTracking().SingleAsync();Check(first.UserId==1&&await db.WebPushDeliveries.CountAsync()==1,"recipient mapping and durable push outbox are correct");
var list=(JsonResult)await one.List(null,default);var json=JsonSerializer.SerializeToElement(list.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));Check(json.GetProperty("unread").GetInt32()==1&&json.GetProperty("items").GetArrayLength()==1,"bell unread count and feed match recipient");
Check(await two.Open(first.Id,default) is NotFoundResult,"another account cannot open notification details");await two.Read(first.Id,false,default);Check((await db.WebNotifications.AsNoTracking().SingleAsync()).ReadAt==null,"another account cannot mark notification read");
await one.Read(first.Id,false,default);Check((await db.WebNotifications.AsNoTracking().SingleAsync()).ReadAt!=null,"owner can mark read");Check(await one.OpenConfirmed(first.Id,default) is LocalRedirectResult redirect&&redirect.Url=="/Projects/Show/4","push and new-tab handoff redirects only through owned local target");
Check(new[]{"Read","Subscribe","Unsubscribe","OpenConfirmed"}.All(name=>typeof(WorkNotificationsController).GetMethod(name)!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>()!=null),"all notification and subscription mutations require anti-forgery tokens");
var services=new ServiceCollection().AddSingleton<IDbContextFactory<AppDbContext>>(factory).AddSingleton(service).BuildServiceProvider();var worker=new WebPushDispatcher(services.GetRequiredService<IServiceScopeFactory>(),new FakeHttpFactory(handler),NullLogger<WebPushDispatcher>.Instance);var dispatch=typeof(WebPushDispatcher).GetMethod("DispatchAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
await (Task)dispatch.Invoke(worker,[CancellationToken.None])!;Check(handler.Calls==1&&handler.Encrypted&&await db.WebPushDeliveries.CountAsync(d=>d.Done)==1,"worker sends encrypted VAPID Web Push through mocked transport and completes outbox");
await service.NotifyEmployeeSafelyAsync(10,"อีกงาน","test","/");handler.Status=HttpStatusCode.Gone;await(Task)dispatch.Invoke(worker,[CancellationToken.None])!;Check(!(await db.WebPushSubscriptions.AsNoTracking().SingleAsync()).Active,"expired 410 push subscription is disabled");
await two.Subscribe(request,default);Check((await db.WebPushSubscriptions.AsNoTracking().SingleAsync()).UserId==2,"shared browser subscription rebinds to current account");
await service.RevokeSessionAsync(2,"test-session");Check(!(await db.WebPushSubscriptions.AsNoTracking().SingleAsync()).Active,"logout revokes only the current account and session device");
await one.Subscribe(request,default);
await service.NotifyEmployeeSafelyAsync(10,"Transient delivery","test","/");handler.Status=HttpStatusCode.ServiceUnavailable;await(Task)dispatch.Invoke(worker,[CancellationToken.None])!;
var retry=await db.WebPushDeliveries.AsNoTracking().OrderByDescending(d=>d.Id).FirstAsync();Check(!retry.Done&&retry.Attempts==1&&retry.NextAttemptAt>DateTime.UtcNow,"transient push failures remain durable and back off");
var calls=handler.Calls;await(Task)dispatch.Invoke(worker,[CancellationToken.None])!;Check(handler.Calls==calls,"retry is not sent again before its due time");
await db.WebPushDeliveries.Where(d=>d.Id==retry.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.NextAttemptAt,DateTime.UtcNow.AddSeconds(-1)));handler.Status=HttpStatusCode.Created;await(Task)dispatch.Invoke(worker,[CancellationToken.None])!;Check(await db.WebPushDeliveries.AnyAsync(d=>d.Id==retry.Id&&d.Done&&d.Attempts==2),"due retry succeeds and completes the same delivery");
await service.NotifyEmployeeSafelyAsync(10,"Queued before account switch","test","/");await two.Subscribe(request,default);calls=handler.Calls;await(Task)dispatch.Invoke(worker,[CancellationToken.None])!;Check(handler.Calls==calls,"pending old-account push cannot reach a rebound browser subscription");
var failedCleanup=new WebNotificationService(new FailingFactory(),new EphemeralDataProtectionProvider(),config,NullLogger<WebNotificationService>.Instance);
var logoutContext=new DefaultHttpContext();var logoutSession=new FakeSession();logoutSession.SetInt32("UserId",1);logoutContext.Features.Set<ISessionFeature>(new FakeSessionFeature{Session=logoutSession});
var auth=new AuthController(db,null!,failedCleanup,NullLogger<AuthController>.Instance){ControllerContext=new ControllerContext{HttpContext=logoutContext}};
var logout=await auth.Logout();Check(logout is RedirectToActionResult action&&action.ActionName=="Login"&&logoutSession.GetInt32("UserId")==null,"Logout clears session and redirects to Login even when push cleanup fails");
var unavailableDb=await factory.CreateDbContextAsync();await unavailableDb.DisposeAsync();logoutSession.SetInt32("UserId",1);
var unavailableAuth=new AuthController(unavailableDb,null!,failedCleanup,NullLogger<AuthController>.Instance){ControllerContext=new ControllerContext{HttpContext=logoutContext}};
Check(await unavailableAuth.Logout() is RedirectToActionResult&&logoutSession.GetInt32("UserId")==null,"Logout still clears session when both account and push housekeeping databases fail");
await one.Subscribe(request,default);
await MeetingChecks.Run(factory,service,telegram);
await OtherEventChecks.Run(factory,service,telegram,config);
await IndependentChannelChecks.Run(factory,service,telegram,config);
await HttpChecks.Run(factory,service,request.Endpoint,request.Keys.P256dh,request.Keys.Auth);
await PushDiagnosticChecks.Run(factory,service);
Console.WriteLine("ALL BACKEND NOTIFICATION CHECKS PASSED (isolated SQLite, no external sends)");

class TestDb(DbContextOptions<AppDbContext> options):AppDbContext(options){protected override void OnModelCreating(ModelBuilder m){var keep=new[]{typeof(LoginUser),typeof(WebNotification),typeof(WebPushSubscription),typeof(WebPushDelivery),typeof(WebPushKey),typeof(NotificationSendLog),typeof(Employee),typeof(EmployeePosition),typeof(MeetingAttendee),typeof(Meeting),typeof(FieldServiceVisit),typeof(FieldServiceAssignee),typeof(CntMCoop)};foreach(var e in m.Model.GetEntityTypes().ToArray())if(!keep.Contains(e.ClrType))m.Ignore(e.ClrType);m.Entity<Meeting>().Ignore(e=>e.Calendar);m.Entity<Meeting>().Ignore(e=>e.Project);m.Entity<Meeting>().HasMany(e=>e.Attendees).WithOne(a=>a.Meeting).HasForeignKey(e=>e.MeetingId);m.Entity<Employee>().Ignore(e=>e.PositionDefinition);m.Entity<EmployeePosition>().Ignore(p=>p.Employees);m.Entity<FieldServiceVisit>().HasOne(v=>v.Coop).WithMany().HasForeignKey(v=>v.CoopId);m.Entity<FieldServiceVisit>().HasMany(v=>v.Assignees).WithOne(a=>a.Visit).HasForeignKey(a=>a.VisitId);m.Entity<FieldServiceAssignee>().HasOne(a=>a.Employee).WithMany().HasForeignKey(a=>a.EmpId);}}
class Factory(DbContextOptions<AppDbContext> options):IDbContextFactory<AppDbContext>{public AppDbContext CreateDbContext()=>new TestDb(options);public Task<AppDbContext>CreateDbContextAsync(CancellationToken ct=default)=>Task.FromResult(CreateDbContext());}
class MysqlSqliteAdapter:DbCommandInterceptor{public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken ct=default){command.CommandText=command.CommandText.Replace("INSERT IGNORE","INSERT OR IGNORE").Replace("ON DUPLICATE KEY UPDATE","ON CONFLICT (EndpointHash) DO UPDATE SET");return ValueTask.FromResult(result);}}
class FakeSession:ISession{readonly Dictionary<string,byte[]> values=new();public string Id=>"test-session";public bool IsAvailable=>true;public IEnumerable<string>Keys=>values.Keys;public void Clear()=>values.Clear();public Task CommitAsync(CancellationToken c=default)=>Task.CompletedTask;public Task LoadAsync(CancellationToken c=default)=>Task.CompletedTask;public void Remove(string k)=>values.Remove(k);public void Set(string k,byte[] v)=>values[k]=v;public bool TryGetValue(string k,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)]out byte[]? v)=>values.TryGetValue(k,out v);}
class FakeUrl:IUrlHelper{public ActionContext ActionContext=>new();public string? Action(UrlActionContext c)=>"/WorkNotifications/"+c.Action;public string?Content(string? c)=>c;public bool IsLocalUrl(string?u)=>u?.StartsWith('/')==true&&!u.StartsWith("//");public string?Link(string?n,object?v)=>"/";public string?RouteUrl(UrlRouteContext c)=>"/";}
class FakeHttp:HttpMessageHandler{public int Calls;public bool Encrypted;public HttpStatusCode Status=HttpStatusCode.Created;protected override async Task<HttpResponseMessage>SendAsync(HttpRequestMessage req,CancellationToken ct){Calls++;Encrypted=req.Headers.Authorization!=null&&req.Content!=null&&!(await req.Content.ReadAsStringAsync(ct)).Contains("คุณมีแจ้งเตือน");return new HttpResponseMessage(Status){Content=new StringContent("")};}}
class FakeHttpFactory(FakeHttp handler):IHttpClientFactory{public HttpClient CreateClient(string name)=>new(handler,false);}

class FakeSessionFeature:ISessionFeature{public ISession Session {get;set;}=new FakeSession();}

class FailingFactory:IDbContextFactory<AppDbContext>{public AppDbContext CreateDbContext()=>throw new InvalidOperationException("simulated push DB outage");public Task<AppDbContext>CreateDbContextAsync(CancellationToken ct=default)=>Task.FromException<AppDbContext>(new InvalidOperationException("simulated push DB outage"));}
