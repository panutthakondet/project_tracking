using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;
using ProjectTracking.Middleware; // ✅ เพิ่ม
using DotNetEnv;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.CookiePolicy;

var builder = WebApplication.CreateBuilder(args);

// ==================================================
// LOAD .env FILE
// - แนะนำ: ใช้ .env เฉพาะตอนพัฒนาในเครื่อง
// - Production ให้ตั้งค่าเป็น Environment Variables ของ Server แทน
// ==================================================
if (builder.Environment.IsDevelopment())
{
    Env.Load();
}

// ==================================================
// ENV HELPER
// ==================================================
string GetEnv(string key)
{
    var v = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(v))
        throw new InvalidOperationException($"❌ Missing environment variable: {key}");
    return v;
}

// ==================================================
// Services
// ==================================================
builder.Services
    .AddControllersWithViews()
    .AddSessionStateTempDataProvider();  // ✅ ให้ TempData ไปอยู่ใน Session ไม่ใช่ Cookie

// ✅ สำคัญ: ให้ View/Service ที่ใช้ IHttpContextAccessor ทำงานได้
builder.Services.AddHttpContextAccessor();

// ==================================================
// Database (MySQL from .env)
// ==================================================
var mysqlConnection = GetEnv("MYSQL_CONNECTION");

// ✅ แนะนำ: ใช้ Factory เป็นตัวหลัก (ปลอดภัยกับ BackgroundService)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(
        mysqlConnection,
        ServerVersion.AutoDetect(mysqlConnection)
    )
);

// ✅ ทำให้ Controllers/Services ที่ยัง inject AppDbContext ใช้งานได้เหมือนเดิม
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext()
);

// ==================================================
// Session
// ==================================================
// ✅ สำคัญ: ต้องมี DistributedMemoryCache เพื่อให้ Session ทำงานได้ครบ
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;

    // ✅ ลดความเสี่ยง session hijacking / CSRF
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// Cookie Policy (ให้ SameSite/Secure มีผลกับคุกกี้อื่น ๆ ด้วย)
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.HttpOnly = HttpOnlyPolicy.Always;
});

// ==================================================
// HTTPS Redirection (แก้ warning หา https port ไม่เจอ)
// ==================================================
// ถ้าในเครื่องคุณใช้ https พอร์ตอื่น เปลี่ยน 5001 ให้ตรง launchSettings.json
var httpsPort = 5001;
var httpsPortEnv = Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT");
if (int.TryParse(httpsPortEnv, out var p) && p > 0) httpsPort = p;

builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = httpsPort;
});

// ==================================================
// Email (SMTP from .env)
// ==================================================
builder.Services.Configure<EmailSettings>(options =>
{
    // ✅ Production ต้องมี SMTP ครบ
    // ✅ Development: ถ้าไม่มี SMTP ให้ปล่อยว่าง (ระบบจะยังรันได้ แต่ส่งเมลไม่ได้)
    if (!builder.Environment.IsDevelopment())
    {
        options.SmtpServer = GetEnv("SMTP_SERVER");
        options.Port = int.Parse(GetEnv("SMTP_PORT"));
        options.Username = GetEnv("SMTP_USERNAME");
        options.Password = GetEnv("SMTP_PASSWORD");
        options.EnableSsl = bool.Parse(GetEnv("SMTP_ENABLE_SSL"));
        options.SenderEmail = GetEnv("SMTP_SENDER_EMAIL");
    }
    else
    {
        options.SmtpServer = Environment.GetEnvironmentVariable("SMTP_SERVER") ?? "";
        options.Port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port : 0;
        options.Username = Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? "";
        options.Password = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? "";
        options.EnableSsl = bool.TryParse(Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL"), out var ssl) && ssl;
        options.SenderEmail = Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL") ?? "";
    }
});

// Email Service
builder.Services.AddScoped<EmailService>();

// Overdue mail service
builder.Services.AddScoped<OverdueMailService>();

// Background Service
builder.Services.AddHostedService<OverdueMailBackgroundService>();

var app = builder.Build();

// ==================================================
// Middleware
// ==================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// ✅ Redirect HTTP -> HTTPS เฉพาะ Production
// ใน Development ถ้าเครื่องไม่มี HTTPS binding/พอร์ต https ไม่ได้ listen จะทำให้เข้าเว็บไม่ได้
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();

// ✅ Security headers (กัน clickjacking / sniffing / ลด referrer)
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

    // CSP แบบเริ่มต้น (ถ้าหน้าใดมี inline script/css เยอะ ให้ค่อย ๆ ปรับเพิ่ม)
    // หมายเหตุ: ตอนนี้มี CDN (Select2) -> allow https:
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

// ✅ Session ต้องมาก่อน Middleware ที่อ่าน session
app.UseSession();

// ✅ (เผื่อในอนาคตใช้ ASP.NET auth) ให้ลำดับถูกต้อง
app.UseAuthentication();
app.UseAuthorization();

// ✅ บังคับ login หลัง Session พร้อม และหลัง auth middleware
app.UseRequireLogin(); // หรือ app.UseMiddleware<RequireLoginMiddleware>();

// ==================================================
// Route
// ==================================================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();