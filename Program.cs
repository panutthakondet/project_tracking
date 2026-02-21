using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;
using ProjectTracking.Middleware;
using DotNetEnv;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// ==================================================
// LOAD .env FILE (ใช้เฉพาะตอนพัฒนาในเครื่อง)
// ==================================================
if (builder.Environment.IsDevelopment())
{
    Env.Load();
}

// ==================================================
// ENV HELPERS
// ==================================================
string GetEnv(string key)
{
    var v = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(v))
        throw new InvalidOperationException($"❌ Missing environment variable: {key}");
    return v;
}

string? GetEnvOrNull(string key) => Environment.GetEnvironmentVariable(key);

int GetEnvIntOrDefault(string key, int defaultValue = 0)
{
    var raw = GetEnvOrNull(key);
    return int.TryParse(raw, out var n) ? n : defaultValue;
}

bool GetEnvBoolOrDefault(string key, bool defaultValue = false)
{
    var raw = GetEnvOrNull(key);
    if (string.IsNullOrWhiteSpace(raw)) return defaultValue;

    if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
    if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
    if (raw == "1") return true;
    if (raw == "0") return false;
    if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
    if (raw.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;

    return defaultValue;
}

// ==================================================
// Services
// ==================================================
builder.Services
    .AddControllersWithViews()
    .AddSessionStateTempDataProvider();

builder.Services.AddHttpContextAccessor();

// ==================================================
// DataProtection (แก้ Session/Antiforgery พังหลังรีสตาร์ท IIS)
// ==================================================
// ✅ ทำให้ key ไม่หายทุกครั้งที่รีสตาร์ท (แก้ warning + Error unprotecting cookie)
var keyPath =
    GetEnvOrNull("DATAPROTECTION_KEYS_PATH")
    ?? (OperatingSystem.IsWindows()
        ? @"C:\inetpub\keys\ProjectTracking"
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aspnet", "DataProtection-Keys", "ProjectTracking"));

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("ProjectTracking");

// ==================================================
// Database (MySQL)
// ==================================================
var mysqlConnection = GetEnv("MYSQL_CONNECTION");

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(
        mysqlConnection,
        ServerVersion.AutoDetect(mysqlConnection)
    )
);

builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext()
);

// ==================================================
// Session
// ==================================================
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;

    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

    options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
});

// ==================================================
// HTTPS Redirection
// ==================================================
var httpsPort = 5001;
var httpsPortEnv = GetEnvOrNull("ASPNETCORE_HTTPS_PORT");
if (int.TryParse(httpsPortEnv, out var p) && p > 0) httpsPort = p;

builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = httpsPort;
});

// ==================================================
// Email (SMTP from env / .env) - ไม่ล่มถ้าขาด
// ==================================================
builder.Services.Configure<EmailSettings>(options =>
{
    options.SmtpServer = GetEnvOrNull("SMTP_SERVER") ?? "";
    options.Port       = GetEnvIntOrDefault("SMTP_PORT", 0);
    options.Username   = GetEnvOrNull("SMTP_USERNAME") ?? "";
    options.Password   = GetEnvOrNull("SMTP_PASSWORD") ?? "";
    options.EnableSsl  = GetEnvBoolOrDefault("SMTP_ENABLE_SSL", false);

    // รองรับทั้งสองชื่อ (คุณถามว่า SMTP_SENDER_EMAIL เหมือน SMTP_FROM ไหม)
    options.SenderEmail =
        GetEnvOrNull("SMTP_SENDER_EMAIL")
        ?? GetEnvOrNull("SMTP_FROM")
        ?? "";

    // ถ้าอยาก “บังคับ Production ต้องมีครบ” ให้เปิดบล็อคนี้
    /*
    if (!builder.Environment.IsDevelopment())
    {
        _ = GetEnv("SMTP_SERVER");
        _ = GetEnv("SMTP_PORT");
        _ = GetEnv("SMTP_USERNAME");
        _ = GetEnv("SMTP_PASSWORD");
        _ = GetEnv("SMTP_ENABLE_SSL");
        _ = GetEnv("SMTP_SENDER_EMAIL"); // หรือ SMTP_FROM
    }
    */
});

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<OverdueMailService>();
builder.Services.AddHostedService<OverdueMailBackgroundService>();
builder.Services.AddHostedService<MeetingReminderBackgroundService>();

var app = builder.Build();

// ==================================================
// Middleware
// ==================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data: blob:; " +
        "style-src 'self' 'unsafe-inline' https:; " +
        "script-src 'self' 'unsafe-inline' https:; " +
        "font-src 'self' data: https:; " +
        "connect-src 'self';";

    await next();
});

app.UseCookiePolicy();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.UseRequireLogin();

// ==================================================
// Route
// ==================================================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();