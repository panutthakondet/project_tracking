using System.Net;
using ProjectTracking.SecurityWorker;
using ProjectTracking.Services.Security;

public static class JsonCookieChecks
{
    sealed class Handler(CookieContainer cookies,bool accept):HttpMessageHandler
    {
        bool authenticated;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.Method==HttpMethod.Post){
                if(request.RequestUri!.AbsolutePath!="/member/login"||request.Content!.Headers.ContentType!.MediaType!="application/json"||request.Headers.GetValues("X-XSRF-TOKEN").Single()!="csrf+value")throw new Exception("Missing JSON CSRF header");
                using var body=System.Text.Json.JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                if(body.RootElement.GetProperty("password").GetString()!="test\"secret")throw new Exception("JSON credential escaping failed");
                authenticated=accept;
                cookies.Add(new Uri("https://example.com"),new Cookie("session","test-session","/"));
                return new HttpResponseMessage(HttpStatusCode.Found){Headers={Location=new Uri("/member/profile",UriKind.Relative)}};
            }
            cookies.Add(new Uri("https://example.com"),new Cookie("XSRF-TOKEN","csrf%2Bvalue","/"));
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(authenticated?"protected-account-marker":"Login page")};
        }
    }
    public static async Task Run(){
        using(var response=System.Text.Json.JsonDocument.Parse("{\"messages\":[{\"responseBody\":\"protected-account-marker\"}]}"))
            if(!JsonCookieLogin.HasIndicator(response.RootElement,"protected-account-marker"))throw new Exception("Completion verification must inspect decoded response bodies");
        foreach(var accept in new[]{true,false}){
            var cookies=new CookieContainer();using var client=new HttpClient(new Handler(cookies,accept));
            var login=new SecurityLoginCredentials{AuthenticationType="JsonCookie",LoginUrl="https://example.com/member/login",Username="test",Password="test\"secret",RequestData="{\"membership_no\":\"{%username%}\",\"password\":\"{%password%}\"}",LoggedInRegex="protected-account-marker"};
            try{var result=await JsonCookieLogin.AuthenticateAsync(client,cookies,login,new Uri("https://example.com/member/profile"),default);if(!accept||!result.Contains("session="))throw new Exception("Session verification failed");}
            catch(InvalidOperationException)when(!accept){}
        }
        Console.WriteLine("PASS JSON Cookie Login: CSRF, JSON escaping, redirects, session and rejected credentials");
    }
}
