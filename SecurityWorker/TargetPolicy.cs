using System.Net;
namespace ProjectTracking.SecurityWorker;
public static class TargetPolicy
{
    public static async Task<Uri> ValidateAsync(string value,string allowedHosts,bool allowPrivate,CancellationToken ct)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo!=""||uri.Fragment!="")throw new InvalidOperationException("Target URL is invalid");
        if(!allowedHosts.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Contains(uri.IdnHost,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Target host is not approved");
        var addresses=await Dns.GetHostAddressesAsync(uri.IdnHost,ct);
        if(addresses.Length==0||addresses.Any(a=>Forbidden(a,allowPrivate)))throw new InvalidOperationException("Target resolves to a prohibited network");
        return uri;
    }
    public static bool Forbidden(IPAddress address,bool allowPrivate)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
        if(IPAddress.IsLoopback(address)||address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any)||address.IsIPv6LinkLocal||address.IsIPv6Multicast)return true;
        var b=address.GetAddressBytes();
        if(b.Length==4){if(b[0]==0||b[0]==127||b[0]>=224||(b[0]==169&&b[1]==254))return true;return !allowPrivate&&(b[0]==10||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168)||(b[0]==100&&b[1]>=64&&b[1]<=127));}
        return !allowPrivate&&(b[0]&0xfe)==0xfc;
    }
}
