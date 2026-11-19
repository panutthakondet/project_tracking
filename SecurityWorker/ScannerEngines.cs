using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProjectTracking.Services.Security;
namespace ProjectTracking.SecurityWorker;
public record EngineResult(string Json,string Version,string Coverage);
public sealed class ScannerEngines(HttpClient zap,HttpClient mobsf,Func<string,string> setting,Action<ScanProgressUpdate>? reportProgress=null)
{
    async Task<JsonElement> Zap(string component,string type,string method,Dictionary<string,string>? args,CancellationToken ct)
    {
        args??=new();args["apikey"]=setting("ZAP_API_KEY");
        var sensitive=type=="action"&&(component is "authentication" or "users" or "sessionManagement");
        using var response=sensitive
            ? await zap.PostAsync($"JSON/{component}/{type}/{method}/",new FormUrlEncodedContent(args),ct)
            : await zap.GetAsync($"JSON/{component}/{type}/{method}/?"+string.Join("&",args.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value))),ct);
        ScannerHttp.RequireSuccess(response,"ZAP "+component+"/"+method);var result=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if(result.TryGetProperty("code",out _))throw new InvalidOperationException("ZAP API rejected the request");return result;
    }
    static bool ContainsTrue(JsonElement e,string key)
    {
        if(e.ValueKind==JsonValueKind.Object){if(e.TryGetProperty(key,out var v)&&v.ToString().Equals("true",StringComparison.OrdinalIgnoreCase))return true;return e.EnumerateObject().Any(p=>ContainsTrue(p.Value,key));}
        return e.ValueKind==JsonValueKind.Array&&e.EnumerateArray().Any(v=>ContainsTrue(v,key));
    }
    static string Value(JsonElement e,string key)=>e.GetProperty(key).ToString();
    async Task Wait(string engine,string id,CancellationToken ct)
    {
        while(true){var status=await Zap(engine,"view","status",new(){["scanId"]=id},ct);if(int.TryParse(Value(status,"status"),out var percent))reportProgress?.Invoke(new(engine=="spider"?"Crawling":"ActiveScanning",Math.Clamp(percent,0,100)));if(Value(status,"status")=="100")return;await Task.Delay(1500,ct);}
    }
    public async Task<EngineResult> Web(SecurityJob job,CancellationToken ct)
    {
        reportProgress?.Invoke(new("Preparing"));
        var target=await TargetPolicy.ValidateAsync(job.TargetUrl??"",job.ApprovedHosts==null?setting("SECURITY_ALLOWED_HOSTS"):string.Join(",",job.ApprovedHosts),setting("SECURITY_ALLOW_PRIVATE_TARGETS")=="true",ct);
        SecurityLoginCredentials? loginProfile=null;
        if(job.RequiresLogin){
            if(string.IsNullOrWhiteSpace(job.AuthProfile)||!Regex.IsMatch(job.AuthProfile,@"^[A-Za-z0-9_-]+$"))throw new InvalidOperationException("Invalid login profile");
            loginProfile=job.Login??JsonSerializer.Deserialize<SecurityLoginCredentials>(await File.ReadAllTextAsync(Path.Combine(setting("SECURITY_AUTH_PROFILES_PATH"),job.AuthProfile+".json"),ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(loginProfile==null||loginProfile.AuthenticationType is not ("Form" or "JsonBearer"))throw new InvalidOperationException("Unsupported login type");
            var origin=target.GetLeftPart(UriPartial.Authority);
            if(!Uri.TryCreate(loginProfile.LoginUrl,UriKind.Absolute,out var loginUrl)||loginUrl.GetLeftPart(UriPartial.Authority)!=origin||loginUrl.UserInfo!=""||loginUrl.Fragment!="")throw new InvalidOperationException("Login URL must use the approved origin");
            if(loginProfile.AuthenticationType=="JsonBearer"){
                if(!JsonLoginVerification.ValidPath(loginProfile.TokenPath))throw new InvalidOperationException("Invalid JSON token field");
                try{using var payload=JsonDocument.Parse(loginProfile.RequestData);if(payload.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();}
                catch(JsonException){throw new InvalidOperationException("JSON Login requires a JSON request body");}
                if(!string.IsNullOrWhiteSpace(loginProfile.ScanStartUrl)){
                    if(!Uri.TryCreate(loginProfile.ScanStartUrl,UriKind.Absolute,out var start)||start.GetLeftPart(UriPartial.Authority)!=origin)throw new InvalidOperationException("Scan start must use the approved origin");
                    target=await TargetPolicy.ValidateAsync(start.AbsoluteUri,job.ApprovedHosts==null?setting("SECURITY_ALLOWED_HOSTS"):string.Join(",",job.ApprovedHosts),setting("SECURITY_ALLOW_PRIVATE_TARGETS")=="true",ct);
                }
            }
        }
        if(job.Mode=="Active"&&(job.Environment!="UAT"||setting("SECURITY_ALLOW_ACTIVE")!="true"))throw new InvalidOperationException("Active scans require UAT and explicit worker configuration");
        var name="security-"+job.Id+"-"+job.LeaseToken[..8];string? context=null,spider=null,active=null;
        try{
            // This ZAP instance is dedicated to one sequential worker: never share it with other users.
            await Zap("core","action","newSession",new(){["name"]="",["overwrite"]="true"},ct);
            context=Value(await Zap("context","action","newContext",new(){["contextName"]=name},ct),"contextId");
            var scope=Regex.Escape(target.GetLeftPart(UriPartial.Authority))+Regex.Escape(target.AbsolutePath.TrimEnd('/'))+@"(?:/.*|\?.*|$)";
            await Zap("context","action","includeInContext",new(){["contextName"]=name,["regex"]=scope},ct);
            if(loginProfile!=null)await Zap("context","action","includeInContext",new(){["contextName"]=name,["regex"]="^"+Regex.Escape(loginProfile.LoginUrl)+"$"},ct);
            await Zap("context","action","excludeFromContext",new(){["contextName"]=name,["regex"]=@"(?i).*\b(logout|signout|delete|remove)\b.*"},ct);
            await Zap("context","action","setContextInScope",new(){["contextName"]=name,["booleanInScope"]="true"},ct);
            await Zap("core","action","setMode",new(){["mode"]="protect"},ct);
            string? user=null;
            if(job.RequiresLogin){
                reportProgress?.Invoke(new("Authenticating"));
                var profile=loginProfile!;
                var jsonBearer=profile.AuthenticationType=="JsonBearer";
                var loggedIn=jsonBearer?JsonLoginVerification.Indicator(profile.TokenPath):profile.LoggedInRegex;
                if(string.IsNullOrWhiteSpace(loggedIn))throw new InvalidOperationException("Login verification is required");
                var config="loginUrl="+Uri.EscapeDataString(profile.LoginUrl)+"&loginRequestData="+Uri.EscapeDataString(profile.RequestData);
                await Zap("authentication","action","setAuthenticationMethod",new(){["contextId"]=context,["authMethodName"]=jsonBearer?"jsonBasedAuthentication":"formBasedAuthentication",["authMethodConfigParams"]=config},ct);
                if(jsonBearer)await Zap("sessionManagement","action","setSessionManagementMethod",new(){["contextId"]=context,["methodName"]="headerBasedSessionManagement",["methodConfigParams"]="headers="+Uri.EscapeDataString("Authorization:Bearer {%json:"+profile.TokenPath+"%}")},ct);
                await Zap("authentication","action","setLoggedInIndicator",new(){["contextId"]=context,["loggedInIndicatorRegex"]=loggedIn},ct);
                user=Value(await Zap("users","action","newUser",new(){["contextId"]=context,["name"]="scanner"},ct),"userId");
                await Zap("users","action","setAuthenticationCredentials",new(){["contextId"]=context,["userId"]=user,["authCredentialsConfigParams"]="username="+Uri.EscapeDataString(profile.Username)+"&password="+Uri.EscapeDataString(profile.Password)},ct);
                await Zap("users","action","setUserEnabled",new(){["contextId"]=context,["userId"]=user,["enabled"]="true"},ct);
                var auth=await Zap("users","action","authenticateAsUser",new(){["contextId"]=context,["userId"]=user},ct);
                if(!ContainsTrue(auth,"authSuccessful")||(jsonBearer&&!JsonLoginVerification.HasIssuedToken(auth,profile.TokenPath)))throw new InvalidOperationException(jsonBearer?"JSON Login failed: no verified token was issued. Check API Login URL, JSON field names and test credentials.":"Form Login failed: authenticated response could not be verified. Check form URL, field names and login indicator.");
            }
            await Zap("spider","action","setOptionProcessForm",new(){["Boolean"]=job.Mode=="Active"?"true":"false"},ct);
            await Zap("spider","action","setOptionPostForm",new(){["Boolean"]=job.Mode=="Active"?"true":"false"},ct);
            var spiderArgs=new Dictionary<string,string>{["url"]=target.AbsoluteUri,["recurse"]="true",["subtreeOnly"]="true"};
            if(user!=null){spiderArgs["contextId"]=context;spiderArgs["userId"]=user;}else spiderArgs["contextName"]=name;
            reportProgress?.Invoke(new("Crawling"));
            spider=Value(await Zap("spider","action",user==null?"scan":"scanAsUser",spiderArgs,ct),"scan");await Wait("spider",spider,ct);
            var crawled=await Zap("spider","view","results",new(){["scanId"]=spider},ct);if(crawled.GetProperty("results").GetArrayLength()==0)throw new InvalidOperationException("Crawler found no URLs; check target connectivity and scope");
            if(job.Mode=="Active"){
                reportProgress?.Invoke(new("ActiveScanning"));
                var args=new Dictionary<string,string>{["url"]=target.AbsoluteUri,["contextId"]=context,["recurse"]="true",["inScopeOnly"]="true"};if(user!=null)args["userId"]=user;
                active=Value(await Zap("ascan","action",user==null?"scan":"scanAsUser",args,ct),"scan");await Wait("ascan",active,ct);
            }
            reportProgress?.Invoke(new("PassiveChecks"));
            while(Value(await Zap("pscan","view","recordsToScan",null,ct),"recordsToScan")!="0")await Task.Delay(1000,ct);
            reportProgress?.Invoke(new("FetchingReport"));
            var collected=await ZapAlertCollector.CollectAsync((start,token)=>Zap("alert","view","alerts",new(){["baseurl"]=target.AbsoluteUri,["start"]=start.ToString(),["count"]="500"},token),ct);
            var version=Value(await Zap("core","view","version",null,ct),"version");
            return new(collected.Json,version,$"ZAP {job.Mode}; {collected.Count} raw alerts collected in full; scoped traditional spider. JavaScript-only routes, MFA and business workflows need separate review. "+(loginProfile?.AuthenticationType=="JsonBearer"?"JSON login verified; context-scoped Bearer session enabled. SPA/browser-only routes still require a browser-based scan. ":user!=null?"Form login configured; confirm authenticated coverage in ZAP before treating the run as complete assurance.":"Unauthenticated scan."));
        }finally{
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try{if(spider!=null)await Zap("spider","action","stop",new(){["scanId"]=spider},cleanup.Token);if(active!=null)await Zap("ascan","action","stop",new(){["scanId"]=active},cleanup.Token);if(context!=null)await Zap("context","action","removeContext",new(){["contextName"]=name},cleanup.Token);}catch{Console.Error.WriteLine("Scanner cleanup failed; restart ZAP before the next run.");}
        }
    }
    public async Task<EngineResult> Mobile(SecurityJob job,string file,CancellationToken ct)
    {
        reportProgress?.Invoke(new("Uploading",0,0,new FileInfo(file).Length));
        using var content=new MultipartFormDataContent();
        var application=ScanUploadContent.Create(file,job.Kind=="Android"?"application/vnd.android.package-archive":"application/octet-stream",reportProgress);
        content.Add(application,"file",job.Kind=="Android"?"application.apk":"application.ipa");
        using var upload=await mobsf.PostAsync("api/v1/upload",content,ct);ScannerHttp.RequireSuccess(upload,"MobSF upload");var info=await upload.Content.ReadFromJsonAsync<JsonElement>(ct);var hash=Value(info,"hash");
        var args=new Dictionary<string,string>{["hash"]=hash,["scan_type"]=Value(info,"scan_type"),["file_name"]=Value(info,"file_name"),["re_scan"]="0"};
        reportProgress?.Invoke(new("Analyzing"));
        using var scan=await mobsf.PostAsync("api/v1/scan",new FormUrlEncodedContent(args),ct);ScannerHttp.RequireSuccess(scan,"MobSF static analysis");
        var scanResult=await scan.Content.ReadFromJsonAsync<JsonElement>(ct);if(scanResult.TryGetProperty("error",out _))throw new InvalidOperationException("MobSF scan failed");
        reportProgress?.Invoke(new("FetchingReport"));
        using var report=await mobsf.PostAsync("api/v1/report_json",new FormUrlEncodedContent(new Dictionary<string,string>{["hash"]=hash}),ct);ScannerHttp.RequireSuccess(report,"MobSF JSON report");var json=await report.Content.ReadAsStringAsync(ct);
        using var parsed=JsonDocument.Parse(json);if(parsed.RootElement.TryGetProperty("error",out _))throw new InvalidOperationException("MobSF report failed");
        var version=parsed.RootElement.TryGetProperty("mobsf_version",out var v)?v.ToString():"MobSF / "+setting("MOBSF_IMAGE_VERSION");
        return new(json,version,"Static analysis only. No emulator, runtime traffic, device exploitation or authenticated API testing. Normalized view covers supported rule sections; review original JSON for additional output. Validate findings manually.");
    }
}
