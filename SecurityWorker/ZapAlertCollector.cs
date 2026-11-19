using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ProjectTracking.Services.Security;
namespace ProjectTracking.SecurityWorker;

public static class ZapAlertCollector
{
    public static async Task<(string Json,int Count)> CollectAsync(Func<int,CancellationToken,Task<JsonElement>> readPage,CancellationToken ct)
    {
        using var buffer=new MemoryStream();using var writer=new Utf8JsonWriter(buffer);
        writer.WriteStartObject();writer.WritePropertyName("alerts");writer.WriteStartArray();
        var count=0;var pages=new HashSet<string>(StringComparer.Ordinal);
        for(var start=0;;start+=500){
            ct.ThrowIfCancellationRequested();var page=await readPage(start,ct);
            if(!page.TryGetProperty("alerts",out var rows)||rows.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("ZAP returned an invalid alerts page");
            var length=rows.GetArrayLength();if(length>500)throw new InvalidOperationException("ZAP ignored the requested report page size");
            if(length>0&&!pages.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rows.GetRawText())))))throw new InvalidOperationException("ZAP repeated a report page; report was not saved to avoid duplicate results");
            foreach(var row in rows.EnumerateArray()){
                if(++count>SecurityReportLimits.MaxRawFindings)throw new InvalidOperationException("Report exceeds 100,000 raw findings; narrow the scan scope and start a new scan");
                row.WriteTo(writer);
                if(writer.BytesCommitted+writer.BytesPending>SecurityReportLimits.MaxReportBytes-2)throw new InvalidOperationException("Report exceeds 50 MB; narrow the scan scope and start a new scan");
            }
            if(length<500)break;
        }
        writer.WriteEndArray();writer.WriteEndObject();writer.Flush();
        return(Encoding.UTF8.GetString(buffer.GetBuffer(),0,(int)buffer.Length),count);
    }
}
