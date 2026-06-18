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
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http.Features;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ==================================================
// LOAD .env FILE
// ==================================================
var envPath = Path.Combine(builder.Environment.ContentRootPath, ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
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
    .AddControllersWithViews(options =>
    {
        options.MaxModelBindingCollectionSize = 10000;
    })
    .AddSessionStateTempDataProvider();

builder.Services.AddHttpContextAccessor();

// ==================================================
// File Upload Limit (รองรับ TOR / BRD ขนาดใหญ่)
// ==================================================
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 209715200; // 200 MB
    options.ValueCountLimit = 10000;
});

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
builder.Services.AddHttpClient<LineMessagingService>();
builder.Services.AddScoped<LineNotificationSettingsService>();
builder.Services.AddHttpClient<TelegramMessagingService>();
builder.Services.AddScoped<TelegramNotificationSettingsService>();
builder.Services.AddScoped<OverdueMailService>();
builder.Services.AddScoped<OverdueNotificationService>();
builder.Services.AddScoped<MeetingNotificationService>();
// Overdue email automation is disabled. We will replace it with in-app bell notifications.
// Keep OverdueMailService registered for existing manual flows and future reference.
builder.Services.AddHostedService<OverdueNotificationBackgroundService>();
builder.Services.AddHostedService<MeetingReminderBackgroundService>();

QuestPDF.Settings.License = LicenseType.Community;
var app = builder.Build();

await EnsureLoginUserProfileColumnAsync(app.Services);
await EnsureActivityCreatedAtColumnsAsync(app.Services);
await EnsureMeetingStatusColumnAsync(app.Services);
await EnsureUserNotificationTableAsync(app.Services);
await EnsureSystemConfigTableAsync(app.Services);
await EnsureLineRecipientTableAsync(app.Services);
await EnsureTelegramRecipientTableAsync(app.Services);
await EnsureMailboxTablesAsync(app.Services);
await EnsureIssueDevStatusValuesAsync(app.Services);
await EnsureSupportOrderStatusValuesAsync(app.Services);

// allow large upload requests
app.Use(async (context, next) =>
{
    var maxBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (maxBodySizeFeature != null && !maxBodySizeFeature.IsReadOnly)
    {
        maxBodySizeFeature.MaxRequestBodySize = 209715200; // 200 MB
    }
    await next();
});

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
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads")
    ),
    RequestPath = "/uploads"
});
app.UseRouting();


app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(self), microphone=(), camera=()";

    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data: blob: https://maps.gstatic.com https://maps.googleapis.com https://*.google.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://maps.googleapis.com https://cdn.jsdelivr.net; " +
        "script-src 'self' 'unsafe-inline' https://maps.googleapis.com https://maps.gstatic.com https://cdn.jsdelivr.net https:; " +
        "font-src 'self' data: https://fonts.gstatic.com https:; " +
        "connect-src 'self' https://maps.googleapis.com https://maps.gstatic.com; " +
        "frame-src https://www.google.com https://maps.google.com;";

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
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();

static async Task EnsureLoginUserProfileColumnAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'login_user'
              AND COLUMN_NAME = 'profile_image_path';";

        var exists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        if (exists > 0) return;

        command.CommandText = "ALTER TABLE login_user ADD COLUMN profile_image_path VARCHAR(500) NULL;";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureActivityCreatedAtColumnsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        foreach (var tableName in new[] { "project", "project_phase", "phase_assign" })
        {
            await EnsureCreatedAtColumnAsync(connection, tableName);
            await EnsureEntryIdColumnAsync(connection, tableName);
        }
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureCreatedAtColumnAsync(System.Data.Common.DbConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $@"
        SELECT COUNT(*)
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = '{tableName}'
          AND COLUMN_NAME = 'created_at';";

    var exists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    if (exists == 0)
    {
        command.CommandText = $"ALTER TABLE `{tableName}` ADD COLUMN created_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP;";
        await command.ExecuteNonQueryAsync();
    }

}

static async Task EnsureEntryIdColumnAsync(System.Data.Common.DbConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $@"
        SELECT COUNT(*)
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = '{tableName}'
          AND COLUMN_NAME = 'entry_id';";

    var exists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    if (exists == 0)
    {
        command.CommandText = $"ALTER TABLE `{tableName}` ADD COLUMN entry_id INT NULL;";
        await command.ExecuteNonQueryAsync();
    }
}

static async Task EnsureMeetingStatusColumnAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'meetings'
              AND COLUMN_NAME = 'status';";

        var exists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        if (exists == 0)
        {
            command.CommandText = "ALTER TABLE `meetings` ADD COLUMN `status` varchar(20) NOT NULL DEFAULT 'ACTIVE';";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            UPDATE `meetings`
            SET `status` = 'ACTIVE'
            WHERE `status` IS NULL OR `status` = '';";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureUserNotificationTableAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `user_notifications` (
              `notification_id` int(11) NOT NULL AUTO_INCREMENT,
              `recipient_user_id` int(11) DEFAULT NULL,
              `recipient_emp_id` int(11) DEFAULT NULL,
              `source_type` varchar(50) NOT NULL,
              `source_id` int(11) NOT NULL,
              `title` varchar(255) NOT NULL,
              `message` text DEFAULT NULL,
              `target_url` varchar(500) DEFAULT NULL,
              `severity` varchar(20) NOT NULL DEFAULT 'WARNING',
              `is_read` tinyint(1) NOT NULL DEFAULT 0,
              `read_at` datetime DEFAULT NULL,
              `is_resolved` tinyint(1) NOT NULL DEFAULT 0,
              `resolved_at` datetime DEFAULT NULL,
              `created_at` datetime NOT NULL DEFAULT current_timestamp(),
              `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`notification_id`),
              UNIQUE KEY `uq_user_notifications_source_emp` (`source_type`,`source_id`,`recipient_emp_id`),
              KEY `idx_user_notifications_recipient` (`recipient_user_id`,`is_read`,`is_resolved`,`created_at`),
              KEY `idx_user_notifications_emp` (`recipient_emp_id`),
              KEY `idx_user_notifications_source` (`source_type`,`source_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";

        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureLineRecipientTableAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `line_recipients` (
              `line_recipient_id` int(11) NOT NULL AUTO_INCREMENT,
              `user_id` int(11) DEFAULT NULL,
              `emp_id` int(11) DEFAULT NULL,
              `recipient_type` varchar(20) NOT NULL DEFAULT 'USER',
              `line_user_id` varchar(100) DEFAULT NULL,
              `line_group_id` varchar(100) DEFAULT NULL,
              `line_display_name` varchar(255) DEFAULT NULL,
              `is_active` tinyint(1) NOT NULL DEFAULT 1,
              `last_followed_at` datetime DEFAULT NULL,
              `last_webhook_at` datetime DEFAULT NULL,
              `created_at` datetime NOT NULL DEFAULT current_timestamp(),
              `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`line_recipient_id`),
              UNIQUE KEY `uq_line_recipients_user_id` (`line_user_id`),
              UNIQUE KEY `uq_line_recipients_group_id` (`line_group_id`),
              KEY `idx_line_recipients_user` (`user_id`,`is_active`),
              KEY `idx_line_recipients_emp` (`emp_id`,`is_active`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";

        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureTelegramRecipientTableAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `telegram_recipients` (
              `telegram_recipient_id` int(11) NOT NULL AUTO_INCREMENT,
              `user_id` int(11) DEFAULT NULL,
              `emp_id` int(11) DEFAULT NULL,
              `recipient_type` varchar(20) NOT NULL DEFAULT 'USER',
              `telegram_user_id` varchar(100) DEFAULT NULL,
              `telegram_chat_id` varchar(100) DEFAULT NULL,
              `telegram_display_name` varchar(255) DEFAULT NULL,
              `is_active` tinyint(1) NOT NULL DEFAULT 1,
              `last_started_at` datetime DEFAULT NULL,
              `last_webhook_at` datetime DEFAULT NULL,
              `created_at` datetime NOT NULL DEFAULT current_timestamp(),
              `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`telegram_recipient_id`),
              UNIQUE KEY `uq_telegram_recipients_user_id` (`telegram_user_id`),
              UNIQUE KEY `uq_telegram_recipients_chat_id` (`telegram_chat_id`),
              KEY `idx_telegram_recipients_user` (`user_id`,`is_active`),
              KEY `idx_telegram_recipients_emp` (`emp_id`,`is_active`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";

        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureSystemConfigTableAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `system_config` (
              `config_key` varchar(100) NOT NULL,
              `config_value` varchar(500) DEFAULT NULL,
              `description` varchar(500) DEFAULT NULL,
              `updated_at` datetime DEFAULT NULL,
              PRIMARY KEY (`config_key`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";

        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureMailboxTablesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `weekly_reports` (
              `report_id` int(11) NOT NULL AUTO_INCREMENT,
              `week_start` date DEFAULT NULL,
              `week_end` date DEFAULT NULL,
              `subject` varchar(255) NOT NULL,
              `summary` text DEFAULT NULL,
              `status` varchar(30) NOT NULL DEFAULT 'DRAFT',
              `created_by_user_id` int(11) DEFAULT NULL,
              `created_by_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime NOT NULL DEFAULT current_timestamp(),
              `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              `sent_to_pm_at` datetime DEFAULT NULL,
              `sent_to_bdm_at` datetime DEFAULT NULL,
              PRIMARY KEY (`report_id`),
              KEY `idx_weekly_reports_creator` (`created_by_user_id`,`status`,`created_at`),
              KEY `idx_weekly_reports_emp` (`created_by_emp_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `weekly_report_attachments` (
              `attachment_id` int(11) NOT NULL AUTO_INCREMENT,
              `report_id` int(11) NOT NULL,
              `file_name` varchar(255) NOT NULL,
              `file_path` varchar(500) NOT NULL,
              `content_type` varchar(150) DEFAULT NULL,
              `file_size` bigint NOT NULL DEFAULT 0,
              `uploaded_by_user_id` int(11) DEFAULT NULL,
              `uploaded_at` datetime NOT NULL DEFAULT current_timestamp(),
              PRIMARY KEY (`attachment_id`),
              KEY `idx_weekly_report_attachments_report` (`report_id`),
              CONSTRAINT `fk_weekly_report_attachments_report`
                FOREIGN KEY (`report_id`) REFERENCES `weekly_reports` (`report_id`)
                ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `mailbox_messages` (
              `message_id` int(11) NOT NULL AUTO_INCREMENT,
              `report_id` int(11) DEFAULT NULL,
              `subject` varchar(255) NOT NULL,
              `body` text DEFAULT NULL,
              `message_type` varchar(50) NOT NULL DEFAULT 'GENERAL',
              `sender_user_id` int(11) DEFAULT NULL,
              `sender_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime NOT NULL DEFAULT current_timestamp(),
              PRIMARY KEY (`message_id`),
              KEY `idx_mailbox_messages_sender` (`sender_user_id`,`created_at`),
              KEY `idx_mailbox_messages_report` (`report_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `mailbox_recipients` (
              `recipient_id` int(11) NOT NULL AUTO_INCREMENT,
              `message_id` int(11) NOT NULL,
              `recipient_user_id` int(11) NOT NULL,
              `recipient_emp_id` int(11) DEFAULT NULL,
              `is_read` tinyint(1) NOT NULL DEFAULT 0,
              `read_at` datetime DEFAULT NULL,
              `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
              `created_at` datetime NOT NULL DEFAULT current_timestamp(),
              PRIMARY KEY (`recipient_id`),
              KEY `idx_mailbox_recipients_user` (`recipient_user_id`,`is_read`,`is_deleted`,`created_at`),
              KEY `idx_mailbox_recipients_message` (`message_id`),
              CONSTRAINT `fk_mailbox_recipients_message`
                FOREIGN KEY (`message_id`) REFERENCES `mailbox_messages` (`message_id`)
                ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureSupportOrderStatusValuesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project_support_order';";

        var tableExists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!tableExists) return;

        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project_support_order'
              AND COLUMN_NAME = 'dev_detail';";

        var hasDevDetail = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!hasDevDetail)
        {
            command.CommandText = @"
                ALTER TABLE `project_support_order`
                  ADD COLUMN `dev_detail` text NULL AFTER `dev_status`;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            ALTER TABLE `project_support_order`
              MODIFY COLUMN `status` varchar(20) NULL DEFAULT 'OPEN',
              MODIFY COLUMN `dev_status` varchar(20) NULL DEFAULT 'TODO';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order`
            SET `status` = CASE
                WHEN `status` = 'WAIT_TEST' THEN 'FIXED'
                WHEN `status` = 'DONE' THEN 'PASS'
                WHEN `status` = 'CLOSE' THEN 'PASS'
                WHEN `status` = 'IN_PROGRESS' THEN 'WIP'
                WHEN `status` IS NULL OR `status` = '' THEN 'OPEN'
                ELSE `status`
            END;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order`
            SET `dev_status` = CASE
                WHEN `dev_status` = 'IN_PROGRESS' THEN 'WIP'
                WHEN `dev_status` IN ('TODO', 'DOING', 'BLOCK') THEN 'WIP'
                WHEN `dev_status` IS NULL OR `dev_status` = '' THEN 'WIP'
                ELSE `dev_status`
            END;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            ALTER TABLE `project_support_order`
              MODIFY COLUMN `status` varchar(20) NOT NULL DEFAULT 'OPEN',
              MODIFY COLUMN `dev_status` varchar(20) NOT NULL DEFAULT 'TODO';";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureIssueDevStatusValuesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;

    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ProjectIssues';";

        var tableExists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!tableExists) return;

        command.CommandText = @"
            UPDATE `ProjectIssues`
            SET `DevStatus` = CASE
                WHEN `DevStatus` IN ('TODO', 'DOING', 'BLOCK') THEN 'WIP'
                WHEN `DevStatus` IS NULL OR `DevStatus` = '' THEN 'WIP'
                ELSE `DevStatus`
            END;";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}
