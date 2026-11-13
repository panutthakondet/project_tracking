using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectTracking.Controllers;
using ProjectTracking.Data;
using ProjectTracking.Services;

static class HttpChecks
{
    public static async Task Run(IDbContextFactory<AppDbContext> factory,WebNotificationService service,string endpoint,string p256dh,string auth)
    {
        using var server=new TestServer(new WebHostBuilder().ConfigureServices(services=>{
            services.AddLogging();services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());services.AddSingleton(factory);services.AddScoped<AppDbContext>(_=>factory.CreateDbContext());services.AddSingleton(service);
            services.AddControllersWithViews().AddApplicationPart(typeof(WorkNotificationsController).Assembly);services.AddAntiforgery();
        }).Configure(app=>{
            app.UseRouting();app.Use(async(context,next)=>{var session=new FakeSession();session.SetInt32("UserId",1);context.Features.Set<ISessionFeature>(new FakeSessionFeature{Session=session});await next();});
            app.UseEndpoints(routes=>{
                routes.MapGet("/test-token",async context=>{var tokens=context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);await context.Response.WriteAsJsonAsync(new{token=tokens.RequestToken});});
                routes.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}");
            });
        }));
        using var client=server.CreateClient();
        var tokenResponse=await client.GetAsync("/WorkNotifications/RequestToken");tokenResponse.EnsureSuccessStatusCode();
        var token=(await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString();
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",tokenResponse.Headers.GetValues("Set-Cookie").Select(s=>s.Split(';')[0])));
        var list=await client.GetAsync("/WorkNotifications/List");if(list.StatusCode!=HttpStatusCode.OK)throw new Exception("HTTP inbox failed "+list.StatusCode);
        var key=await client.GetAsync("/WorkNotifications/PushKey");if(key.StatusCode!=HttpStatusCode.OK)throw new Exception("HTTP push key failed "+key.StatusCode);
        object body=new{endpoint,expirationTime=(long?)null,keys=new{p256dh,auth}};
        var rejected=await client.PostAsJsonAsync("/WorkNotifications/Subscribe",body);if(rejected.StatusCode!=HttpStatusCode.BadRequest)throw new Exception("Missing CSRF accepted");
        using var request=new HttpRequestMessage(HttpMethod.Post,"/WorkNotifications/Subscribe"){Content=JsonContent.Create(body)};request.Headers.Add("RequestVerificationToken",token);
        var response=await client.SendAsync(request);if(response.StatusCode!=HttpStatusCode.OK)throw new Exception("Browser-shaped subscription HTTP failed "+response.StatusCode+" "+await response.Content.ReadAsStringAsync());
        await using(var keyDb=await factory.CreateDbContextAsync())
        {
            var stored=await keyDb.WebPushKeys.AsNoTracking().SingleAsync();
            await keyDb.WebPushKeys.ExecuteUpdateAsync(s=>s.SetProperty(k=>k.ProtectedPrivateKey,"invalid-protected-key"));
            var unavailable=await client.GetAsync("/WorkNotifications/PushKey");
            var problem=await unavailable.Content.ReadFromJsonAsync<JsonElement>();
            if(unavailable.StatusCode!=HttpStatusCode.ServiceUnavailable||problem.GetProperty("code").GetString()!="PUSH_KEY_UNAVAILABLE")throw new Exception("Key failure diagnostic missing");
            await keyDb.WebPushKeys.ExecuteUpdateAsync(s=>s.SetProperty(k=>k.ProtectedPrivateKey,stored.ProtectedPrivateKey));
            Console.WriteLine("PASS unreadable VAPID private key returns a safe diagnostic code and trace instead of an opaque failure");
        }
        Console.WriteLine("PASS real MVC HTTP inbox/key routes and browser JSON subscription with CSRF cookie/header; missing token rejected");
    }
}
