using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace ProjectTracking.Services.Security;
public static class SecurityReportParser
{
    public static string Plain(string value)=>Regex.Replace(value,"<[^>]+>"," ").Trim();
    private static string Text(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)?v.ValueKind==JsonValueKind.String?v.GetString()??"":v.ToString():"";
    public static string Fingerprint(string engine,ScanObservation o)
    {
        var location=o.Location.Trim();var parameter="";var suffix=location.LastIndexOf(" [param: ",StringComparison.Ordinal);if(suffix>=0){parameter=location[suffix..];location=location[..suffix];}
        if(Uri.TryCreate(location,UriKind.Absolute,out var uri))location=uri.GetLeftPart(UriPartial.Path)+"?"+string.Join("&",uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=')[0]).OrderBy(x=>x,StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(engine+"\n"+o.RuleId+"\n"+location+parameter)));
    }
    public static List<ScanObservation> Parse(string engine,string json)
    {
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=64});var root=doc.RootElement;var result=new List<ScanObservation>();
        void Add(ScanObservation observation){if(result.Count>=10000)throw new InvalidOperationException("รายงานมีประเด็นมากเกิน 10,000 รายการ");result.Add(observation);}
        if(engine=="ZAP"){
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("alerts",out var alerts)||alerts.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("รูปแบบรายงาน ZAP ไม่ถูกต้อง");
            foreach(var alert in alerts.EnumerateArray()){
                var rule=Text(alert,"pluginId");if(rule=="")rule=Text(alert,"pluginid");
                var title=Text(alert,"alert");if(title=="")title=Text(alert,"name");
                if(string.IsNullOrWhiteSpace(rule)||string.IsNullOrWhiteSpace(title))throw new InvalidOperationException("รายงาน ZAP ขาดรหัสหรือชื่อประเด็น");
                var severity=Text(alert,"risk");if(severity=="")severity=Text(alert,"riskcode");
                var locations=alert.TryGetProperty("instances",out var instances)&&instances.ValueKind==JsonValueKind.Array?instances.EnumerateArray().ToArray():[alert];
                foreach(var item in locations){var url=Text(item,"uri");if(url=="")url=Text(item,"url");var parameter=Text(item,"param");
                    Add(new(rule,title,SecurityValues.Severity(severity),url+(parameter!=""?" [param: "+parameter+"]":""),Plain(Text(alert,"description")),Text(item,"evidence"),Plain(Text(alert,"solution")),Plain(Text(alert,"reference")),Text(alert,"cweid")));
                }
            }
        }else if(engine=="MobSF"){
            if(root.ValueKind!=JsonValueKind.Object||root.TryGetProperty("error",out _))throw new InvalidOperationException("รายงาน MobSF ไม่สำเร็จ");
            var recognized=false;
            foreach(var section in new[]{"code_analysis","binary_analysis","manifest_analysis","security_analysis","network_security"}){
                if(!root.TryGetProperty(section,out var group))continue;if(group.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))throw new InvalidOperationException("รูปแบบส่วนวิเคราะห์ MobSF ไม่ถูกต้อง");recognized=true;
                if(group.ValueKind==JsonValueKind.Object&&group.TryGetProperty("findings",out var nested))group=nested;
                if(group.ValueKind==JsonValueKind.Object&&group.TryGetProperty("manifest_findings",out nested))group=nested;
                if(group.ValueKind==JsonValueKind.Object&&group.TryGetProperty("network_findings",out nested))group=nested;
                var entries=group.ValueKind==JsonValueKind.Object?group.EnumerateObject().Select(p=>(p.Name,p.Value)).ToArray():group.ValueKind==JsonValueKind.Array?group.EnumerateArray().Select((v,i)=>(section+"-"+i,v)).ToArray():[];
                foreach(var (key,value) in entries){
                    if(value.ValueKind!=JsonValueKind.Object)continue;
                    var meta=value.TryGetProperty("metadata",out var m)&&m.ValueKind==JsonValueKind.Object?m:value;
                    var severity=Text(meta,"severity");if(severity is "good" or "secure")continue;
                    var title=Text(meta,"description");if(title=="")title=Text(value,"title");if(title=="")title=Text(value,"name");if(title=="")title=key;
                    var rule=Text(value,"rule");if(rule=="")rule=group.ValueKind==JsonValueKind.Array?Plain(title):key;
                    var files=value.TryGetProperty("files",out var f)&&f.ValueKind==JsonValueKind.Object?f.EnumerateObject().Select(p=>(p.Name,p.Value.ToString())).ToArray():[(section,Text(value,"description"))];
                    foreach(var (location,evidence) in files)Add(new(section+":"+rule,Plain(title),SecurityValues.Severity(severity),location,Plain(Text(value,"description")),evidence,Plain(Text(meta,"fix")) is {Length:>0} fix?fix:Plain(Text(meta,"recommendation")),Plain(Text(meta,"ref")),Text(meta,"cwe")));
                }
            }
            if(!recognized)throw new InvalidOperationException("รายงาน MobSF ไม่มีส่วนผลวิเคราะห์ที่รองรับ ไม่สามารถสรุปว่าไม่พบช่องโหว่ได้");
        }else throw new InvalidOperationException("ไม่รองรับเครื่องมือสแกนนี้");
        return result.Select(o=>o with{RuleId=SecurityValues.Cut(o.RuleId,200),Title=SecurityValues.Cut(o.Title,500),Location=SecurityValues.Cut(o.Location,4000),Cwe=SecurityValues.Cut(o.Cwe,100),Description=SecurityValues.Cut(o.Description,50000),Evidence=SecurityValues.Cut(o.Evidence,50000),Remediation=SecurityValues.Cut(o.Remediation,50000),References=SecurityValues.Cut(o.References,20000)}).GroupBy(o=>Fingerprint(engine,o)).Select(g=>g.First()).ToList();
    }
}
