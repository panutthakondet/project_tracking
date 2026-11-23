using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using ProjectTracking.Controllers;
using ProjectTracking.Helpers;
using ProjectTracking.Middleware;
using System.Reflection;

public static class PermissionChecks
{
 public static void Run()
 {
  var keys=MenuScanner.ScanMenus().Select(x=>x.Key).ToHashSet();
  foreach(var key in new[]{"Index","Edit","CreateIssues","ViewAllProjects","ApproveHosts","Delete"})Check(keys.Contains("SecurityScans."+key),"permissions page discovers SecurityScans."+key);
  bool Allowed(string action,string menus,string role="USER"){
   var session=new MemorySession();session.SetInt32("UserId",1);session.SetString("Role",role);session.SetString("Menus",menus);
   var http=new DefaultHttpContext{Session=session};
   var context=new AuthorizationFilterContext(new ActionContext(http,new RouteData(),new ActionDescriptor()),new List<IFilterMetadata>());
   var type=typeof(SecurityScansController);
   foreach(var attr in type.GetCustomAttributes<RequireMenuAttribute>().Concat(type.GetMethod(action)!.GetCustomAttributes<RequireMenuAttribute>())){attr.OnAuthorization(context);if(context.Result!=null)return false;}
   return true;
  }
  Check(!Allowed("DeleteScan","SecurityScans.Index,SecurityScans.Edit"),"scan editors cannot delete history without delete grant");
  Check(Allowed("DeleteScan","SecurityScans.Index,SecurityScans.Delete"),"separate delete grant permits history deletion");
  Check(!Allowed("ApproveHost","SecurityScans.Index,SecurityScans.Edit"),"scan editor cannot approve a new target without separate grant");
  Check(Allowed("ApproveHost","SecurityScans.Index,SecurityScans.ApproveHosts"),"designated approver can approve targets without edit grant");
  Check(Allowed("Index","SecurityScans.Index"),"read access uses its own grant without Projects.Index");
  Check(!Allowed("SaveWebsiteTarget","SecurityScans.Index"),"read-only users cannot change Website section credentials");
  Check(Allowed("SaveWebsiteTarget","SecurityScans.Index,SecurityScans.Edit"),"Website section configuration requires scan editing permission");
  Check(!Allowed("ApproveWebsiteTarget","SecurityScans.Index,SecurityScans.Edit"),"Website section approvals require the separate approval permission");
  Check(!Allowed("SaveLogin","SecurityScans.Index"),"read-only grant cannot set scanner credentials");
  Check(Allowed("SaveLogin","SecurityScans.Index,SecurityScans.Edit"),"security editor can configure login");
  Check(!Allowed("Queue","SecurityScans.Index"),"read-only grant cannot queue scans");
  Check(!Allowed("Queue","Projects.Index,Projects.Edit"),"project editing alone cannot manage security scans");
  Check(Allowed("Queue","SecurityScans.Index,SecurityScans.Edit"),"explicit security edit grant permits scan management");
  Check(!Allowed("Index","SecurityScans.ViewAllProjects"),"cross-project scope does not bypass page permission");
  Check(!Allowed("CreateIssues","SecurityScans.Index,SecurityScans.CreateIssues"),"issue conversion still requires ProjectIssues.Create");
  Check(Allowed("CreateIssues","SecurityScans.Index,SecurityScans.CreateIssues,ProjectIssues.Create"),"issue conversion accepts both explicit grants");
  Check(Allowed("Queue","","ADMIN"),"administrator access remains supported");
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
 class MemorySession:ISession
 {
  readonly Dictionary<string,byte[]> values=new();public string Id=>"permissions-test";public bool IsAvailable=>true;public IEnumerable<string> Keys=>values.Keys;
  public void Clear()=>values.Clear();public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;public Task LoadAsync(CancellationToken ct=default)=>Task.CompletedTask;
  public void Remove(string key)=>values.Remove(key);public void Set(string key,byte[] value)=>values[key]=value;public bool TryGetValue(string key,out byte[] value)=>values.TryGetValue(key,out value!);
 }
}
