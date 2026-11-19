using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTracking.SecurityWorker;

public static class JsonLoginVerification
{
    public static bool ValidPath(string path) => path.Length<=120 && Regex.IsMatch(path,@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$");
    public static string Indicator(string path) => "\""+Regex.Escape(path.Split('.').Last())+"\"\\s*:\\s*\"[^\"\\s]+\"";
    private static string? Find(JsonElement element,string key)
    {
        if(element.ValueKind==JsonValueKind.Object){
            if(element.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String)return value.GetString();
            foreach(var property in element.EnumerateObject()){var found=Find(property.Value,key);if(found!=null)return found;}
        }else if(element.ValueKind==JsonValueKind.Array){foreach(var item in element.EnumerateArray()){var found=Find(item,key);if(found!=null)return found;}}
        return null;
    }
    public static bool HasIssuedToken(JsonElement auth,string path)
    {
        if(!ValidPath(path))return false;
        var header=Find(auth,"responseHeader");
        if(header==null||!Regex.IsMatch(header,@"^HTTP/\d(?:\.\d)?\s+2\d\d\b"))return false;
        try{
            using var json=JsonDocument.Parse(Find(auth,"responseBody")??"");
            var value=json.RootElement;
            foreach(var part in path.Split('.'))if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty(part,out value))return false;
            var token=value.ValueKind==JsonValueKind.String?value.GetString():null;
            return token is {Length:>0 and <=16000} && Regex.IsMatch(token,@"^[A-Za-z0-9._~+/=\-]+$");
        }catch(JsonException){return false;}
    }
}
