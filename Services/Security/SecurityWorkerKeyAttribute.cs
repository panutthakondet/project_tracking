using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;
namespace ProjectTracking.Services.Security;
[AttributeUsage(AttributeTargets.Class)]
public class SecurityWorkerKeyAttribute:Attribute,IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config=context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected=Environment.GetEnvironmentVariable("SECURITY_WORKER_KEY")??config["SECURITY_WORKER_KEY"];
        if(string.IsNullOrWhiteSpace(expected)||expected.Length<32){context.Result=new StatusCodeResult(503);return;}
        var supplied=context.HttpContext.Request.Headers.Authorization.ToString();
        if(!supplied.StartsWith("Bearer ",StringComparison.Ordinal)||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied[7..])),SHA256.HashData(Encoding.UTF8.GetBytes(expected))))context.Result=new UnauthorizedResult();
    }
}
