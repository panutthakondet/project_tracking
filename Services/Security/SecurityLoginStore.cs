using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTracking.Services.Security;

public sealed class SecurityLoginStore(SecurityStorage storage, IDataProtectionProvider protection)
{
    public static bool IsManaged(string? name) => name != null && Regex.IsMatch(name, @"^web-[a-f0-9]{32}$");
    public static void Validate(SecurityLoginCredentials login, string? targetUrl)
    {
        var target = SecurityScanService.ValidateUrl(targetUrl);
        var url = SecurityScanService.ValidateUrl(login.LoginUrl);
        if (target.GetLeftPart(UriPartial.Authority) != url.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("URL Login ต้องใช้โดเมน พอร์ต และ HTTP/HTTPS เดียวกับ URL ระบบ");
        if (login.AuthenticationType is not ("Form" or "JsonBearer" or "JsonCookie"))
            throw new InvalidOperationException("รูปแบบ Login ไม่ถูกต้อง");
        if (!string.IsNullOrWhiteSpace(login.ScanStartUrl) && SecurityScanService.ValidateUrl(login.ScanStartUrl).GetLeftPart(UriPartial.Authority) != target.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("URL เริ่มตรวจต้องอยู่บนเว็บไซต์เดียวกับระบบ");
        if (login.AuthenticationType=="JsonCookie" && (string.IsNullOrWhiteSpace(login.ScanStartUrl)||new Uri(login.ScanStartUrl).AbsolutePath==url.AbsolutePath))
            throw new InvalidOperationException("JSON Cookie Login ต้องระบุเส้นทางหน้าที่ต้อง Login จริง และไม่ใช่หน้า Login");
        if (login.AuthenticationType is "JsonBearer" or "JsonCookie")
        {
            if (login.AuthenticationType == "JsonBearer" && (!Regex.IsMatch(login.TokenPath, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$") || login.TokenPath.Length > 120))
                throw new InvalidOperationException("ชื่อ Token ใช้ชื่อฟิลด์ JSON เช่น token หรือ data.access_token");
            try {
                using var payload = JsonDocument.Parse(login.RequestData);
                if (payload.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            } catch (JsonException) { throw new InvalidOperationException("ข้อมูล Login แบบ JSON ต้องเป็น JSON object ที่ถูกต้อง"); }
        }
        if (string.IsNullOrWhiteSpace(login.Username) || login.Username.Length > 255 || string.IsNullOrEmpty(login.Password) || login.Password.Length > 2000)
            throw new InvalidOperationException("กรอก Username และ Password ของบัญชีทดสอบให้ครบ");
        if (login.RequestData.Length > 4000 || !login.RequestData.Contains("{%username%}") || !login.RequestData.Contains("{%password%}"))
            throw new InvalidOperationException("ข้อมูลฟอร์มต้องมี {%username%} และ {%password%}");
        if (login.AuthenticationType == "JsonBearer") return;
        if (string.IsNullOrWhiteSpace(login.LoggedInRegex) || login.LoggedInRegex.Length > 500)
            throw new InvalidOperationException("ระบุข้อความตรวจสอบหลัง Login สำเร็จ ไม่เกิน 500 ตัวอักษร");
        try { _ = new Regex(login.LoggedInRegex, RegexOptions.None, TimeSpan.FromSeconds(1)); }
        catch (ArgumentException) { throw new InvalidOperationException("รูปแบบข้อความตรวจสอบ Login ไม่ถูกต้อง"); }
    }
    private IDataProtector Protector(long systemId) => protection.CreateProtector("SecurityLogin.v1", systemId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public async Task<string> SaveAsync(long systemId, SecurityLoginCredentials login, string? targetUrl, CancellationToken ct, string? existingProfile = null)
    {
        if (string.IsNullOrEmpty(login.Password) && IsManaged(existingProfile))
            login.Password = (await ReadAsync(systemId, existingProfile!, targetUrl, ct)).Password;
        Validate(login, targetUrl);
        var encrypted = Protector(systemId).Protect(JsonSerializer.Serialize(login));
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(encrypted));
        var file = await storage.WriteAsync(input, ".login", 64000, ct);
        // Unique immutable profile keeps already queued scans on their original credentials.
        return "web-" + Path.GetFileNameWithoutExtension(file.Key);
    }
    public async Task<SecurityLoginCredentials> ReadAsync(long systemId, string profile, string? targetUrl, CancellationToken ct)
    {
        if (!IsManaged(profile)) throw new InvalidOperationException("Invalid managed login profile");
        try
        {
            var encrypted = await File.ReadAllTextAsync(storage.PathFor(profile[4..] + ".login"), ct);
            var login = JsonSerializer.Deserialize<SecurityLoginCredentials>(Protector(systemId).Unprotect(encrypted));
            if (login == null) throw new InvalidOperationException();
            Validate(login, targetUrl);
            return login;
        }
        catch (Exception error) when (error is IOException or CryptographicException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("อ่านข้อมูล Login ไม่ได้ กรุณาบันทึกการตั้งค่า Login ใหม่ และตรวจสอบ Data Protection keys");
        }
    }
    public void DeleteUncommitted(string profile) { if (IsManaged(profile)) storage.Delete(profile[4..] + ".login"); }
}
