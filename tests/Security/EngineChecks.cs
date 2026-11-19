using ProjectTracking.SecurityWorker;
using ProjectTracking.Services.Security;
using System.Net;
using System.Text;
public static class EngineChecks
{
 public static async Task Run()
 {
  using var handler=new EngineStub();using var client=new HttpClient(handler){BaseAddress=new Uri("http://engine/")};
  var engines=new ScannerEngines(client,client,key=>key=="SECURITY_ALLOWED_HOSTS"?"93.184.216.34":"test");
  var job=new SecurityJob(1,"12345678lease","WebInformation","ZAP","Passive","1",null,"UAT","https://93.184.216.34/",false,null,null,null);
  var result=await engines.Web(job,default);
  if(SecurityReportParser.Parse("ZAP",result.Json).Count!=0||handler.Paths.Any(p=>p.Contains("ascan/action/scan"))||!handler.Paths.Any(p=>p.Contains("removeContext")))throw new Exception("Passive workflow or cleanup incorrect");
  Console.WriteLine("PASS ZAP passive adapter executes scope, crawl, passive drain, report and cleanup with fake HTTP");
  var dynamicEngines=new ScannerEngines(client,client,k=>k=="SECURITY_ALLOWED_HOSTS"?throw new Exception("Dynamic approval read legacy env list"):"test");
  await dynamicEngines.Web(job with{ApprovedHosts=["93.184.216.34"]},default);
  try{await engines.Web(job with{ApprovedHosts=[]},default);throw new Exception("Empty server approval fell back to local allowlist");}catch(InvalidOperationException e)when(e.Message=="Target host is not approved"){}
  Console.WriteLine("PASS dynamic approval works without environment changes; empty server approval cannot fall back to a permissive local list");
  var profiles=Path.Combine(Path.GetTempPath(),"security-auth-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(profiles);
  try{
   await File.WriteAllTextAsync(Path.Combine(profiles,"member.json"),"""{"loginUrl":"https://93.184.216.34/login","requestData":"username={%username%}&password={%password%}","loggedInRegex":"Logout","username":"fixture","password":"fixture"}""");
   var authenticated=new ScannerEngines(client,client,k=>k=="SECURITY_AUTH_PROFILES_PATH"?profiles:k=="SECURITY_ALLOWED_HOSTS"?"93.184.216.34":"test");
   try{await authenticated.Web(job with{RequiresLogin=true,AuthProfile="member"},default);throw new Exception("Failed authentication was accepted");}catch(InvalidOperationException){Console.WriteLine("PASS authenticated scan rejects failed login verification");}
  }finally{Directory.Delete(profiles,true);}

  handler.AuthSuccess=true;
  var webLogin=new SecurityLoginCredentials{LoginUrl="https://93.184.216.34/login",Username="embedded-test",Password="embedded-secret",LoggedInRegex="Logout"};
  var webAuthenticated=new ScannerEngines(client,client,k=>k=="SECURITY_AUTH_PROFILES_PATH"?throw new Exception("Web login must not read local profiles"):k=="SECURITY_ALLOWED_HOSTS"?"93.184.216.34":"test");
  await webAuthenticated.Web(job with{RequiresLogin=true,AuthProfile="web-"+new string('a',32),Login=webLogin},default);
  if(!handler.EmbeddedCredentialsSeen)throw new Exception("Embedded credentials were not passed to ZAP");
  Console.WriteLine("PASS web-managed credentials authenticate through ZAP without a local profile file");
  var jsonLogin=new SecurityLoginCredentials{AuthenticationType="JsonBearer",LoginUrl="https://93.184.216.34/api/member/auth/login",ScanStartUrl="https://93.184.216.34/member/",Username="fixture",Password="fixture",RequestData="""{"membership_no":"{%username%}","mem_password":"{%password%}"}"""};
  string Response(string body,string status="200 OK")=>System.Text.Json.JsonSerializer.Serialize(new{authSuccessful="true",responseHeader="HTTP/1.1 "+status+"\r\n",responseBody=body});
  handler.AuthResponse=Response("""{"token":"fixture.jwt.token"}""");
  var jsonJob=job with{RequiresLogin=true,AuthProfile="web-"+new string('b',32),Login=jsonLogin};
  var verified=await webAuthenticated.Web(jsonJob,default);
  if(!handler.JsonConfigured||!handler.HeaderConfigured||!verified.Coverage.Contains("JSON login verified"))throw new Exception("JSON Bearer session not configured");
  foreach(var invalid in new[]{Response("{}"),Response("""{"token":""}"""),Response("""{"token":"fixture"}""","401 Unauthorized"),Response("not JSON"),Response("""{"token":"bad token"}""")}){
   handler.AuthResponse=invalid;var before=handler.Paths.Count(p=>p.Contains("spider/action/scanAsUser"));
   var rejected=false;try{await webAuthenticated.Web(jsonJob,default);}catch(InvalidOperationException){rejected=true;}
   if(!rejected||handler.Paths.Count(p=>p.Contains("spider/action/scanAsUser"))!=before)throw new Exception("Unverified JSON login started crawling");
  }
  Console.WriteLine("PASS JSON login uses native context Bearer session; missing, empty, invalid and unauthorized tokens stop before crawling");
  handler.AuthResponse=null;
  var file=Path.GetTempFileName();try{
   await File.WriteAllTextAsync(file,"fixture");var mobile=await engines.Mobile(job with{Engine="MobSF",Kind="Android",Mode="Static"},file,default);
   if(SecurityReportParser.Parse("MobSF",mobile.Json).Count!=0||!handler.Paths.Any(p=>p.Contains("api/v1/scan")))throw new Exception("MobSF workflow incorrect");
   Console.WriteLine("PASS MobSF adapter uploads, requests static scan and retrieves JSON with fake HTTP");
   await engines.Mobile(job with{Engine="MobSF",Kind="iOS",Mode="Static"},file,default);
   Console.WriteLine("PASS APK and IPA upload parts include supported MIME types and filenames");
   foreach(var failure in new[]{("/api/v1/upload","MobSF upload"),("/api/v1/scan","MobSF static analysis"),("/api/v1/report_json","MobSF JSON report")}){
    handler.FailPath=failure.Item1;
    try{await engines.Mobile(job with{Engine="MobSF",Kind="Android",Mode="Static"},file,default);throw new Exception("Expected failed engine request");}
    catch(ScannerRequestException error){
     if(error.Stage!=failure.Item2||error.StatusCode!=HttpStatusCode.BadRequest||error.Message.Contains("fixture-private-response"))throw new Exception("Stage or redaction incorrect");
    }
   }
   handler.FailPath=null;
   if(ScannerHttp.IsTransient(new ScannerRequestException("completion",HttpStatusCode.BadRequest))||!ScannerHttp.IsTransient(new ScannerRequestException("completion",HttpStatusCode.ServiceUnavailable))||!ScannerHttp.IsTransient(new HttpRequestException("fixture")))throw new Exception("Incorrect retry classification");
   Console.WriteLine("PASS scanner HTTP failures identify stages without exposing response bodies; only transient completion failures retry");
  }finally{File.Delete(file);}
 }
 class EngineStub:HttpMessageHandler
 {
  public List<string> Paths{get;}=[];
  public string? FailPath{get;set;}
  public bool AuthSuccess{get;set;}
  public bool EmbeddedCredentialsSeen{get;set;}
  public string? AuthResponse{get;set;}
  public bool JsonConfigured,HeaderConfigured;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   var path=request.RequestUri!.AbsolutePath;Paths.Add(path);
   if(path=="/JSON/core/action/setMode/"){
    var query=System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
    if(query["mode"]!="protect")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("{\"code\":\"illegal_parameter\"}")});
   }
   if(path=="/JSON/users/action/setAuthenticationCredentials/"){
    if(request.Method!=HttpMethod.Post || request.RequestUri.Query.Contains("password"))throw new Exception("Credentials must use POST");
    var credentials=System.Web.HttpUtility.ParseQueryString(request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult())["authCredentialsConfigParams"];
    EmbeddedCredentialsSeen=credentials=="username=embedded-test&password=embedded-secret";
   }
   if(path=="/JSON/authentication/action/setAuthenticationMethod/"){
    var data=System.Web.HttpUtility.ParseQueryString(request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult());
    JsonConfigured=data["authMethodName"]=="jsonBasedAuthentication" && System.Web.HttpUtility.ParseQueryString(data["authMethodConfigParams"]!)["loginRequestData"]!.Contains("membership_no");
   }
   if(path=="/JSON/sessionManagement/action/setSessionManagementMethod/"){
    var data=System.Web.HttpUtility.ParseQueryString(request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult());
    HeaderConfigured=data["methodName"]=="headerBasedSessionManagement"&&System.Web.HttpUtility.ParseQueryString(data["methodConfigParams"]!)["headers"]=="Authorization:Bearer {%json:token%}";
   }
   if(path=="/api/v1/upload") {
    var form=request.Content as MultipartFormDataContent??throw new Exception("Expected multipart upload");
    var part=form.Single(p=>p.Headers.ContentDisposition?.Name?.Trim('"')=="file");
    var name=part.Headers.ContentDisposition!.FileName?.Trim('"');
    var expected=name=="application.apk"?"application/vnd.android.package-archive":name=="application.ipa"?"application/octet-stream":throw new Exception("Unexpected upload filename");
    if(part.Headers.ContentType?.MediaType!=expected)throw new Exception("MobSF rejects uploads without a supported MIME type");
   }
   if(path==FailPath)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("fixture-private-response")});
   var json=path switch{
    "/JSON/context/action/newContext/"=>"{\"contextId\":\"1\"}",
    "/JSON/spider/action/scanAsUser/"=>"{\"scan\":\"1\"}",
    "/JSON/spider/action/scan/"=>"{\"scan\":\"1\"}",
    "/JSON/spider/view/results/"=>"{\"results\":[\"https://93.184.216.34/\"]}",
    "/JSON/spider/view/status/"=>"{\"status\":\"100\"}",
    "/JSON/pscan/view/recordsToScan/"=>"{\"recordsToScan\":\"0\"}",
    "/JSON/alert/view/alerts/"=>"{\"alerts\":[]}",
    "/JSON/users/action/newUser/"=>"{\"userId\":\"1\"}",
    "/JSON/users/action/authenticateAsUser/"=>AuthResponse??(AuthSuccess?"{\"authSuccessful\":\"true\"}":"{\"authSuccessful\":\"false\"}"),
    "/JSON/core/view/version/"=>"{\"version\":\"fake\"}",
    "/api/v1/upload"=>"{\"hash\":\"abc\",\"scan_type\":\"apk\",\"file_name\":\"application.apk\"}",
    "/api/v1/report_json"=>"{\"code_analysis\":{\"findings\":{}},\"mobsf_version\":\"fake\"}",
    _=>"{\"Result\":\"OK\"}"
   };
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
  }
 }
}
