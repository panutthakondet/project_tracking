using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ProjectTracking.Models;
using ProjectTracking.ViewModels.Security;
public static class RazorChecks
{
 public static async Task Run()
 {
  Console.WriteLine("Razor: building isolated services");
  var builder=WebApplication.CreateEmptyBuilder(new WebApplicationOptions{ApplicationName=typeof(ProjectTracking.Controllers.SecurityScansController).Assembly.GetName().Name,ContentRootPath=Directory.GetCurrentDirectory(),EnvironmentName="Testing",WebRootPath=Path.Combine(Directory.GetCurrentDirectory(),"wwwroot")});
  builder.WebHost.UseKestrel();builder.Configuration.Sources.Clear();builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProjectTracking.Controllers.SecurityScansController).Assembly);builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
  await using var app=builder.Build();Console.WriteLine("Razor: services ready");app.MapControllerRoute("default","{controller}/{action}/{id?}");
  var project=new Project{ProjectId=1,ProjectName="Security UI — isolated test fixture"};var system=new SecuritySystem{Id=1,ProjectId=1,Name="Web Member UAT",TargetUrl="https://uat.example.com/"};var version=new SecurityVersion{Id=1,SystemId=1,VersionName="1.0.0",BuildNumber="42"};var scan=new SecurityScan{Id=1,VersionId=1,Status="Completed",EngineVersion="Test fixture",Coverage="UI test fixture only — no live scan"};
  var mobile=new SecuritySystem{Id=2,ProjectId=1,Name="Application Android — ชื่อระบบสำหรับตรวจหน้าจอ",Kind="Android"};
  var mobileVersion=new SecurityVersion{Id=2,SystemId=2,VersionName="1.0.0",Environment="UAT",ChangeNotes="ทดสอบความปลอดภัยแอปพลิเคชัน"};
  var index=new SecurityProjectPage{Project=project,Systems=[system,mobile,new(){Id=3,ProjectId=1,Name="เว็บไซต์ที่รออนุมัติ",TargetUrl="https://pending.example.com/"}],Versions=[version,mobileVersion],Artifacts=[new(){Id=1,VersionId=2,OriginalName="application-release-long-name.apk",Sha256=new string('A',64),Size=100352000}],Scans=Enumerable.Range(1,31).Reverse().Select(i=>new SecurityScan{Id=i,VersionId=1,Engine="ZAP",Status=i%3==0?"Failed":"Completed"}).ToList(),CanManage=true,WorkerConfigured=true,CanApproveHosts=true,HostApprovals=[new(){ProjectId=1,Host="uat.example.com",IsApproved=true,UpdatedBy=1}]};
  index.LoginSettings[1]=new SecurityLoginSettings{LoginUrl="https://uat.example.com/auth",Username="fixture-display-user",LoggedInRegex="Sign out",RequestData="member={%username%}&pass={%password%}"};
  var detail=new SecurityScanPage{TotalObservations=10001,PageNumber=1,SeverityCounts=new(){{"Medium",10001}},Project=project,System=system,Version=version,Scan=scan,Observations=[new(){Id=1,ScanId=1,FindingId=1,Title="ตรวจพบหัวข้อความปลอดภัยที่ยังไม่ได้ตั้งค่า",Severity="Medium",RuleId="100",Location="https://uat.example.com/account",Description="รายละเอียดจากผลตรวจ",Remediation="เพิ่มหัวข้อ HTTP ตามคำแนะนำของเครื่องมือ"}],Findings=[new(){Id=1,SystemId=1,Title="Missing header"}],CanManage=true,CanCreateIssue=true,Employees=[new(){EmpId=1,EmpName="พนักงานทดสอบ UI"}]};
  var output=Path.Combine(Path.GetTempPath(),"security-ui-preview");Directory.CreateDirectory(output);
  var running=new SecurityScanPage{Project=project,System=system,Version=version,Scan=new SecurityScan{Id=3,VersionId=1,Engine="MobSF",Status="Running",StartedAt=DateTime.UtcNow.AddMinutes(-3)},Progress=new SecurityScanProgress{ScanId=3,Stage="Analyzing",UpdatedAt=DateTime.UtcNow},CanManage=true};
  foreach(var pair in new (string,object)[]{("Index",index),("Scan",detail),("Scan-running",running)}){
   using var scope=app.Services.CreateScope();var http=new DefaultHttpContext{RequestServices=scope.ServiceProvider};http.Request.Scheme="http";http.Request.Host=new HostString("127.0.0.1",8766);
   var route=new RouteData();route.Routers.Add(new PreviewRouter());route.Values["controller"]="SecurityScans";route.Values["action"]=pair.Item1;
   var action=new ActionContext(http,route,new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
   Console.WriteLine("Razor: resolving view "+pair.Item1);
   var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();var view=engine.GetView(null,$"/Views/SecurityScans/{(pair.Item1=="Index"?"Index":"Scan")}.cshtml",false);if(!view.Success)throw new Exception(string.Join(",",view.SearchedLocations));
   using var writer=new StringWriter();var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=pair.Item2};
   var temp=new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
   Console.WriteLine("Razor: rendering "+pair.Item1);
   await view.View.RenderAsync(new ViewContext(action,view.View,data,temp,writer,new HtmlHelperOptions()));
   var html="<!doctype html><html lang='th'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><link rel='stylesheet' href='bootstrap.css'><link rel='stylesheet' href='site.css'><link rel='stylesheet' href='security.css'><style>body{padding:16px;background:#eef3f9}.index-hero{background:#10264b;color:white}main{max-width:1400px;margin:auto}</style><main>"+writer+"</main></html>";
   await File.WriteAllTextAsync(Path.Combine(output,pair.Item1+".html"),html);
   if(!html.Contains("__RequestVerificationToken")||!html.Contains("SecurityScans/"))throw new Exception("Missing CSRF token or generated routes");
   if(pair.Item1=="Index"&&(!html.Contains("fixture-display-user")||!html.Contains("https://uat.example.com/auth")||!html.Contains("data-login-optional-password=\"true\"")||html.Contains("fixture-secret")))throw new Exception("Login settings must be displayed without the password");
   if(pair.Item1=="Scan"&&(!html.Contains("page=2")||!html.Contains("10,001")))throw new Exception("Paged scan must retain navigation and whole-run totals");
   Console.WriteLine("PASS compiled Razor "+pair.Item1+" renders with CSRF and routes");
  }
  File.Copy("wwwroot/css/site.css",Path.Combine(output,"site.css"),true);
  File.Copy("wwwroot/lib/bootstrap/dist/css/bootstrap.min.css",Path.Combine(output,"bootstrap.css"),true);File.Copy("wwwroot/css/security-center.css",Path.Combine(output,"security.css"),true);
 }
 class PreviewRouter:IRouter
 {
  public Task RouteAsync(RouteContext context)=>Task.CompletedTask;
  public VirtualPathData? GetVirtualPath(VirtualPathContext context){var values=context.Values;var controller=values["controller"]?.ToString()??"SecurityScans";var action=values["action"]?.ToString()??"Index";var path=controller+"/"+action+(values.TryGetValue("id",out var id)&&id!=null?"/"+id:"");var query=values.Where(p=>p.Key!="controller"&&p.Key!="action"&&p.Key!="id"&&p.Value!=null).Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value!.ToString()!));return new VirtualPathData(this,path+(query.Any()?"?"+string.Join("&",query):""));}
 }
}
