using System.Net;
using System.Net.Http.Headers;
using ProjectTracking.Services.Security;
namespace ProjectTracking.SecurityWorker;

public sealed class ScanUploadContent(string path,Action<ScanProgressUpdate>? report):HttpContent
{
    private readonly long length=new FileInfo(path).Length;
    public static ScanUploadContent Create(string path,string mime,Action<ScanProgressUpdate>? report)
    {
        var content=new ScanUploadContent(path,report);
        content.Headers.ContentType=new MediaTypeHeaderValue(mime);
        return content;
    }
    protected override bool TryComputeLength(out long value){value=length;return true;}
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>WriteAsync(stream,CancellationToken.None);
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken ct)=>WriteAsync(stream,ct);
    private async Task WriteAsync(Stream target,CancellationToken ct)
    {
        await using var input=File.OpenRead(path);var buffer=new byte[81920];long done=0;int count;
        while((count=await input.ReadAsync(buffer,ct))>0){
            await target.WriteAsync(buffer.AsMemory(0,count),ct);done+=count;
            report?.Invoke(new("Uploading",length>0?(int)Math.Clamp(done*100d/length,0,100):null,done,length));
        }
    }
}
