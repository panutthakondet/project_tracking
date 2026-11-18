using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ProjectTracking.SecurityWorker;
using ProjectTracking.Services.Security;
static string Setting(string name)=>Environment.GetEnvironmentVariable(name)??"";
var baseUrl=Setting("SECURITY_APP_URL");var key=Setting("SECURITY_WORKER_KEY");
if(!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||key.Length<32)throw new InvalidOperationException("Set HTTPS SECURITY_APP_URL and SECURITY_WORKER_KEY (at least 32 characters)");
using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
using var api=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(baseUrl.TrimEnd('/')+"/"),Timeout=TimeSpan.FromMinutes(3)};api.DefaultRequestHeaders.Authorization=new("Bearer",key);
using var zap=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(Setting("ZAP_URL")),Timeout=TimeSpan.FromMinutes(2)};
using var mobsf=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(Setting("MOBSF_URL")),Timeout=TimeSpan.FromMinutes(25)};mobsf.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",Setting("MOBSF_API_KEY"));
var engines=new ScannerEngines(zap,mobsf,Setting);
Console.WriteLine("Waiting for scanner APIs before claiming jobs");
while(!stop.IsCancellationRequested){
 try{using var z=await zap.GetAsync("JSON/core/view/version/?apikey="+Uri.EscapeDataString(Setting("ZAP_API_KEY")),stop.Token);z.EnsureSuccessStatusCode();using var m=await mobsf.GetAsync("",stop.Token);if((int)m.StatusCode>=500)throw new HttpRequestException();break;}
 catch(OperationCanceledException)when(stop.IsCancellationRequested){return;}
 catch{try{await Task.Delay(5000,stop.Token);}catch(OperationCanceledException){return;}}
}

while(!stop.IsCancellationRequested){
    SecurityJob? job=null;
    try{
        using var claim=await api.PostAsync("api/security-worker/claim",null,stop.Token);
        if(claim.StatusCode==HttpStatusCode.NoContent){await Task.Delay(5000,stop.Token);continue;}
        claim.EnsureSuccessStatusCode();job=await claim.Content.ReadFromJsonAsync<SecurityJob>(stop.Token)??throw new InvalidOperationException("Invalid job");
        Console.WriteLine($"Starting scan {job.Id} ({job.Engine})");
        using var work=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);work.CancelAfter(TimeSpan.FromMinutes(30));
        using var heartbeatStop=CancellationTokenSource.CreateLinkedTokenSource(work.Token);
        var heartbeat=Task.Run(async()=>{try{while(true){await Task.Delay(TimeSpan.FromSeconds(30),heartbeatStop.Token);using var response=await api.PostAsJsonAsync($"api/security-worker/heartbeat/{job.Id}",new{job.LeaseToken},heartbeatStop.Token);response.EnsureSuccessStatusCode();}}catch(OperationCanceledException)when(heartbeatStop.IsCancellationRequested){}catch{work.Cancel();}},CancellationToken.None);
        string? file=null;
        try{
            EngineResult result;
            if(job.Engine=="ZAP")result=await engines.Web(job,work.Token);
            else if(job.Engine=="MobSF"){
                file=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+(job.Kind=="Android"?".apk":".ipa"));
                using var request=new HttpRequestMessage(HttpMethod.Get,$"api/security-worker/artifact/{job.Id}");request.Headers.Add("X-Scan-Lease",job.LeaseToken);
                using var download=await api.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,work.Token);download.EnsureSuccessStatusCode();
                await using(var input=await download.Content.ReadAsStreamAsync(work.Token))await using(var output=File.Create(file)){
                    var buffer=new byte[81920];long size=0;int count;
                    while((count=await input.ReadAsync(buffer,work.Token))>0){size+=count;if(size>250L*1024*1024)throw new InvalidOperationException("Artifact size limit exceeded");await output.WriteAsync(buffer.AsMemory(0,count),work.Token);}
                }
                await using(var input=File.OpenRead(file)){var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,work.Token));if(!hash.Equals(job.ArtifactSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Artifact checksum mismatch");}
                result=await engines.Mobile(job,file,work.Token);
            }else throw new InvalidOperationException("Unsupported engine");
            for(var attempt=0;;attempt++){
                using var form=new MultipartFormDataContent();form.Add(new StringContent(job.LeaseToken),"leaseToken");form.Add(new StringContent(result.Version),"engineVersion");form.Add(new StringContent(result.Coverage),"coverage");form.Add(new StringContent(result.Json),"report","report.json");
                try{using var complete=await api.PostAsync($"api/security-worker/complete/{job.Id}",form,work.Token);complete.EnsureSuccessStatusCode();break;}catch(HttpRequestException)when(attempt<2){await Task.Delay(2000,work.Token);}
            }
            Console.WriteLine($"Completed scan {job.Id}");
        }finally{heartbeatStop.Cancel();await heartbeat;if(file!=null)File.Delete(file);}
    }catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
    catch(Exception error){
        // Do not log exception messages or request URLs: engine requests may contain credentials.
        var reason=error is InvalidOperationException?error.Message:error is HttpRequestException http&&http.StatusCode.HasValue?"Engine/API HTTP "+(int)http.StatusCode.Value:error.GetType().Name;
        Console.Error.WriteLine($"Scan {job?.Id} failed: {reason}");
        if(job!=null)try{using var response=await api.PostAsJsonAsync($"api/security-worker/fail/{job.Id}",new{job.LeaseToken,Error="Scanner failed: "+reason+". Check engine configuration, target allowlist and engine health; start a new scan after correction."},stop.Token);}catch{Console.Error.WriteLine("Could not report failure; the lease will expire.");}
        try{await Task.Delay(5000,stop.Token);}catch(OperationCanceledException){break;}
    }
}
