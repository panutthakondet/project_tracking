using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProjectTracking.Services.Security;

namespace ProjectTracking.SecurityWorker;

public static class JsonCookieLogin
{
    public static bool HasIndicator(JsonElement response,string pattern)
    {
        if(response.ValueKind==JsonValueKind.Object){
            if(response.TryGetProperty("responseBody",out var body)&&body.ValueKind==JsonValueKind.String)return Regex.IsMatch(body.GetString()??"",pattern,RegexOptions.None,TimeSpan.FromSeconds(1));
            return response.EnumerateObject().Any(p=>HasIndicator(p.Value,pattern));
        }
        return response.ValueKind==JsonValueKind.Array&&response.EnumerateArray().Any(p=>HasIndicator(p,pattern));
    }
    // Redirects are followed manually so cookies and credentials never leave the approved origin.
    public static async Task<string> AuthenticateAsync(HttpClient client, CookieContainer cookies, SecurityLoginCredentials profile, Uri start, CancellationToken ct)
    {
        var login = new Uri(profile.LoginUrl);
        var origin = login.GetLeftPart(UriPartial.Authority);
        if(start.GetLeftPart(UriPartial.Authority)!=origin || start.AbsolutePath==login.AbsolutePath)
            throw new InvalidOperationException("JSON Cookie Login: specify a protected start URL different from the Login page");
        var indicator = new Regex(profile.LoggedInRegex, RegexOptions.None, TimeSpan.FromSeconds(1));
        async Task<string> Read(HttpRequestMessage request,bool anonymous=false)
        {
            for(var hop=0;hop<6;hop++)
            {
                using var response=await client.SendAsync(request,ct);
                if((int)response.StatusCode is >=300 and <400)
                {
                    var location=response.Headers.Location??throw new InvalidOperationException("Login redirect has no location");
                    var next=new Uri(request.RequestUri!,location);
                    if(next.GetLeftPart(UriPartial.Authority)!=origin || next.UserInfo!="" || next.Fragment!="")throw new InvalidOperationException("Login redirected outside the approved origin");
                    // Never replay a password POST on 307/308.
                    if(response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)throw new InvalidOperationException("Login requires an unsupported POST redirect");
                    request.Dispose();request=new HttpRequestMessage(HttpMethod.Get,next);continue;
                }
                if(anonymous && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)return "";
                ScannerHttp.RequireSuccess(response,"JSON Cookie Login");
                return await response.Content.ReadAsStringAsync(ct);
            }
            throw new InvalidOperationException("Too many Login redirects");
        }
        using(var preflight=new HttpRequestMessage(HttpMethod.Get,login))await Read(preflight);
        // A public page must not count as proof of authentication.
        using(var anonymous=new HttpRequestMessage(HttpMethod.Get,start))
            if(indicator.IsMatch(await Read(anonymous,true)))throw new InvalidOperationException("Login indicator also matches before Login; choose a protected-page-only indicator");
        var csrf=cookies.GetCookies(login)["XSRF-TOKEN"]?.Value;
        if(string.IsNullOrEmpty(csrf))throw new InvalidOperationException("JSON Cookie Login: XSRF-TOKEN cookie was not issued by the Login page");
        var payload=profile.RequestData;
        // Escape values as JSON string content rather than interpolating raw credentials.
        payload=payload.Replace("{%username%}",JsonSerializer.Serialize(profile.Username)[1..^1]).Replace("{%password%}",JsonSerializer.Serialize(profile.Password)[1..^1]);
        using var parsed=JsonDocument.Parse(payload);
        using(var post=new HttpRequestMessage(HttpMethod.Post,login))
        {
            post.Content=new StringContent(payload,Encoding.UTF8,"application/json");
            post.Headers.TryAddWithoutValidation("X-XSRF-TOKEN",Uri.UnescapeDataString(csrf));
            post.Headers.TryAddWithoutValidation("X-Requested-With","XMLHttpRequest");
            post.Headers.TryAddWithoutValidation("Origin",origin);
            post.Headers.Referrer=login;
            await Read(post);
        }
        using(var verify=new HttpRequestMessage(HttpMethod.Get,start))
            if(!indicator.IsMatch(await Read(verify)))throw new InvalidOperationException("JSON Cookie Login failed: protected-page indicator was not found after Login");
        var header=cookies.GetCookieHeader(start);
        if(string.IsNullOrEmpty(header))throw new InvalidOperationException("JSON Cookie Login did not establish session cookies");
        return header;
    }
}
