using System.Security.Cryptography;
using System.IO.Compression;
namespace ProjectTracking.Services.Security;
public class SecurityStorage
{
    private readonly string root;
    public SecurityStorage(IConfiguration config,IWebHostEnvironment env)
    {
        root=Path.GetFullPath(config["SECURITY_STORAGE_PATH"]??Environment.GetEnvironmentVariable("SECURITY_STORAGE_PATH")??Path.Combine(env.ContentRootPath,"App_Data","Security"));
        var web=Path.GetFullPath(env.WebRootPath??Path.Combine(env.ContentRootPath,"wwwroot"));
        if(root==web||root.StartsWith(web+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Security storage must be outside wwwroot.");
    }
    public string PathFor(string key)
    {
        if(string.IsNullOrEmpty(key)||key.Length>100||key.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='.'&&c!='-')||key.Contains(".."))throw new InvalidOperationException("Invalid storage key.");
        return Path.Combine(root,key);
    }
    public async Task<(string Key,string Hash,long Size)> WriteAsync(Stream input,string extension,long max,CancellationToken ct)
    {
        Directory.CreateDirectory(root);var key=Guid.NewGuid().ToString("N")+extension;var path=PathFor(key);long size=0;
        try{
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){
                var buffer=new byte[81920];int n;while((n=await input.ReadAsync(buffer,ct))>0){size+=n;if(size>max)throw new InvalidOperationException("ไฟล์มีขนาดเกินที่กำหนด");hash.AppendData(buffer,0,n);await output.WriteAsync(buffer.AsMemory(0,n),ct);}
            }
            if(size==0)throw new InvalidOperationException("ไฟล์ว่าง");
            return(key,Convert.ToHexString(hash.GetHashAndReset()),size);
        }catch{if(File.Exists(path))File.Delete(path);throw;}
    }
    public void Delete(string key){var path=PathFor(key);if(File.Exists(path))File.Delete(path);}
    public void ValidateApplication(string key,string kind)
    {
        using var zip=ZipFile.OpenRead(PathFor(key));
        if(zip.Entries.Count>100000)throw new InvalidOperationException("ไฟล์แอปมีจำนวนรายการมากเกินไป");
        var valid=kind=="Android"?zip.GetEntry("AndroidManifest.xml")!=null:zip.Entries.Any(e=>e.FullName.StartsWith("Payload/",StringComparison.Ordinal)&&e.FullName.EndsWith(".app/Info.plist",StringComparison.Ordinal));
        if(!valid)throw new InvalidOperationException("โครงสร้างไฟล์ไม่ตรงกับ APK/IPA ที่เลือก");
    }
}
