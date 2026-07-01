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
builder.Services.AddScoped<StatusApprovalService>();
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
await EnsureProjectPmEmpIdColumnAsync(app.Services);
await EnsureStatusApprovalRequestTableAsync(app.Services);
await EnsureMeetingStatusColumnAsync(app.Services);
await EnsureUserNotificationTableAsync(app.Services);
await EnsureNotificationSendLogTableAsync(app.Services);
await EnsureProjectFollowupCreatedByColumnAsync(app.Services);
await EnsureSystemConfigTableAsync(app.Services);
await EnsureLineRecipientTableAsync(app.Services);
await EnsureTelegramRecipientTableAsync(app.Services);
await EnsureWeeklyReportTablesAsync(app.Services);
await EnsureRequirementBoardTablesAsync(app.Services);
await EnsureIssueDevStatusValuesAsync(app.Services);
await EnsureSupportOrderStatusValuesAsync(app.Services);
await EnsureTestScenarioReadyStatusValuesAsync(app.Services);
await EnsureDevGitHistoryTablesAsync(app.Services);

if (args.Contains("--cleanup-statuses", StringComparer.OrdinalIgnoreCase))
{
    await PrintStatusCleanupSummaryAsync(app.Services);
    Console.WriteLine("ProjectIssues and SupportOrders status cleanup completed.");
    return;
}

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
        "img-src 'self' data: blob: https://maps.gstatic.com https://maps.googleapis.com https://*.google.com https://images.unsplash.com https://plus.unsplash.com; " +
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

static async Task EnsureProjectPmEmpIdColumnAsync(IServiceProvider services)
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
              AND TABLE_NAME = 'project'
              AND COLUMN_NAME = 'pm_emp_id';";

        var exists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        if (exists == 0)
        {
            command.CommandText = "ALTER TABLE `project` ADD COLUMN `pm_emp_id` INT NULL AFTER `ba_emp_id`;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project'
              AND INDEX_NAME = 'idx_project_pm_emp_id';";

        var indexExists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        if (indexExists == 0)
        {
            command.CommandText = "CREATE INDEX `idx_project_pm_emp_id` ON `project` (`pm_emp_id`);";
            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureStatusApprovalRequestTableAsync(IServiceProvider services)
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
            CREATE TABLE IF NOT EXISTS `status_approval_requests` (
                `request_id` INT NOT NULL AUTO_INCREMENT,
                `target_type` VARCHAR(30) NOT NULL,
                `target_id` INT NOT NULL,
                `project_id` INT NULL,
                `project_name` VARCHAR(255) NULL,
                `target_title` VARCHAR(500) NULL,
                `current_status` VARCHAR(50) NULL,
                `requested_status` VARCHAR(50) NOT NULL,
                `request_status` VARCHAR(20) NOT NULL DEFAULT 'PENDING',
                `request_note` VARCHAR(1000) NULL,
                `requested_by_user_id` INT NULL,
                `requested_by_emp_id` INT NULL,
                `requested_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                `reviewed_by_user_id` INT NULL,
                `reviewed_by_emp_id` INT NULL,
                `reviewed_at` DATETIME NULL,
                `review_note` VARCHAR(1000) NULL,
                `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (`request_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
        await command.ExecuteNonQueryAsync();

        await EnsureStatusApprovalIndexAsync(
            connection,
            "idx_status_approval_target_status",
            "CREATE INDEX `idx_status_approval_target_status` ON `status_approval_requests` (`target_type`, `target_id`, `request_status`);");

        await EnsureStatusApprovalIndexAsync(
            connection,
            "idx_status_approval_project_status",
            "CREATE INDEX `idx_status_approval_project_status` ON `status_approval_requests` (`project_id`, `request_status`);");

        await EnsureStatusApprovalIndexAsync(
            connection,
            "idx_status_approval_requested_at",
            "CREATE INDEX `idx_status_approval_requested_at` ON `status_approval_requests` (`requested_at`);");
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureStatusApprovalIndexAsync(
    System.Data.Common.DbConnection connection,
    string indexName,
    string createSql)
{
    using var command = connection.CreateCommand();
    command.CommandText = $@"
        SELECT COUNT(*)
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'status_approval_requests'
          AND INDEX_NAME = '{indexName}';";

    var exists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    if (exists > 0) return;

    command.CommandText = createSql;
    await command.ExecuteNonQueryAsync();
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

static async Task EnsureNotificationSendLogTableAsync(IServiceProvider services)
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
            CREATE TABLE IF NOT EXISTS `notification_send_logs` (
              `log_id` bigint(20) NOT NULL AUTO_INCREMENT,
              `channel` varchar(20) NOT NULL,
              `recipient_emp_id` int(11) DEFAULT NULL,
              `recipient_address` varchar(255) DEFAULT NULL,
              `title` varchar(255) NOT NULL,
              `message` text DEFAULT NULL,
              `target_url` varchar(500) DEFAULT NULL,
              `sent_at` datetime NOT NULL DEFAULT current_timestamp(),
              PRIMARY KEY (`log_id`),
              KEY `idx_notification_send_logs_channel_sent` (`channel`,`sent_at`),
              KEY `idx_notification_send_logs_emp` (`recipient_emp_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";

        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureProjectFollowupCreatedByColumnAsync(IServiceProvider services)
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
              AND TABLE_NAME = 'project_followups';";

        var tableExists = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!tableExists) return;

        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project_followups'
              AND COLUMN_NAME = 'created_by_emp_id';";

        var hasCreatedBy = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!hasCreatedBy)
        {
            command.CommandText = @"
                ALTER TABLE `project_followups`
                  ADD COLUMN `created_by_emp_id` int(11) NULL AFTER `owner_emp_id`;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            UPDATE `project_followups`
            SET `status` = 'OPEN'
            WHERE `status` = 'IN_PROGRESS';";
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

        command.CommandText = @"
            INSERT INTO `system_config` (`config_key`, `config_value`, `description`, `updated_at`)
            VALUES
              ('MEETING_NOTIFICATION_RUN_AT', '06:00', 'Meeting Auto - เวลาส่งแจ้งเตือนประชุมอัตโนมัติ เวลาไทย', NOW()),
              ('OVERDUE_NOTIFICATION_RISK_DAYS', '7', 'Overdue Auto - จำนวนวันล่วงหน้าที่ถือว่าเสี่ยงล่าช้า', NOW()),
              ('OVERDUE_NOTIFICATION_RUN_AT', '07:00', 'Overdue Auto - เวลาส่งแจ้งเตือนงานล่าช้า/เสี่ยงล่าช้า เวลาไทย', NOW())
            ON DUPLICATE KEY UPDATE `config_key` = VALUES(`config_key`);";

        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureWeeklyReportTablesAsync(IServiceProvider services)
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
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project_support_order'
              AND COLUMN_NAME = 'is_reopen';";

        var hasIsReopen = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!hasIsReopen)
        {
            command.CommandText = @"
                ALTER TABLE `project_support_order`
                  ADD COLUMN `is_reopen` tinyint(1) NOT NULL DEFAULT 0 AFTER `dev_detail`;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project_support_order'
              AND COLUMN_NAME = 'reopen_count';";

        var hasReopenCount = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!hasReopenCount)
        {
            command.CommandText = @"
                ALTER TABLE `project_support_order`
                  ADD COLUMN `reopen_count` int NOT NULL DEFAULT 0 AFTER `is_reopen`;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            ALTER TABLE `project_support_order`
              MODIFY COLUMN `status` varchar(20) NULL DEFAULT 'OPEN',
              MODIFY COLUMN `dev_status` varchar(20) NULL DEFAULT 'WIP';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order`
            SET `status` = CASE
                WHEN `status` IN ('OPEN', 'PASS', 'FAIL', 'REJECT') THEN `status`
                WHEN `status` IN ('WAIT_TEST', 'WIP', 'FIXED', 'IN_PROGRESS', 'TODO', 'DOING', 'BLOCK') THEN 'OPEN'
                WHEN `status` IN ('DONE', 'CLOSE', 'CLOSED', 'RESOLVED') THEN 'PASS'
                WHEN `status` IS NULL OR `status` = '' THEN 'OPEN'
                ELSE 'OPEN'
            END;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order`
            SET `dev_status` = CASE
                WHEN `dev_status` = 'FIXED' THEN 'FIXED'
                WHEN `dev_status` IN ('TODO', 'DOING', 'BLOCK', 'IN_PROGRESS', 'OPEN', 'FAIL', 'PASS', 'REJECT') THEN 'WIP'
                WHEN `dev_status` IS NULL OR `dev_status` = '' THEN 'WIP'
                ELSE 'WIP'
            END;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order`
            SET `is_reopen` = CASE
                    WHEN COALESCE(`reopen_count`, 0) > 0 THEN 1
                    ELSE COALESCE(`is_reopen`, 0)
                END,
                `reopen_count` = COALESCE(`reopen_count`, 0);";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order`
            SET `dev_status` = 'FIXED'
            WHERE `status` = 'PASS'
              AND `dev_status` <> 'FIXED';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order`
            SET `dev_status` = 'WIP'
            WHERE `status` = 'FAIL'
              AND `dev_status` <> 'WIP';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            ALTER TABLE `project_support_order`
              MODIFY COLUMN `status` varchar(20) NOT NULL DEFAULT 'OPEN',
              MODIFY COLUMN `dev_status` varchar(20) NOT NULL DEFAULT 'WIP';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `project_support_order_status_histories` (
              `id` int NOT NULL AUTO_INCREMENT,
              `order_id` int NOT NULL,
              `old_status` varchar(20) NULL,
              `new_status` varchar(20) NOT NULL DEFAULT 'OPEN',
              `is_reopen` tinyint(1) NOT NULL DEFAULT 0,
              `reopen_count` int NOT NULL DEFAULT 0,
              `changed_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
              `changed_by_emp_id` int NULL,
              PRIMARY KEY (`id`),
              KEY `IX_project_support_order_status_histories_order_id` (`order_id`),
              KEY `IX_project_support_order_status_histories_changed_at` (`changed_at`),
              KEY `IX_project_support_order_status_histories_order_id_changed_at` (`order_id`, `changed_at`),
              CONSTRAINT `FK_support_order_status_histories_order`
                FOREIGN KEY (`order_id`) REFERENCES `project_support_order` (`order_id`)
                ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            INSERT INTO `project_support_order_status_histories`
                (`order_id`, `old_status`, `new_status`, `is_reopen`, `reopen_count`, `changed_at`, `changed_by_emp_id`)
            SELECT
                o.`order_id`,
                NULL,
                COALESCE(o.`status`, 'OPEN'),
                COALESCE(o.`is_reopen`, 0),
                COALESCE(o.`reopen_count`, 0),
                COALESCE(o.`created_at`, NOW()),
                o.`created_by`
            FROM `project_support_order` o
            WHERE NOT EXISTS (
                SELECT 1
                FROM `project_support_order_status_histories` h
                WHERE h.`order_id` = o.`order_id`
            );";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `project_support_order` o
            LEFT JOIN (
                SELECT `order_id`, COUNT(*) AS `fail_count`
                FROM `project_support_order_status_histories`
                WHERE `new_status` = 'FAIL'
                GROUP BY `order_id`
            ) h ON h.`order_id` = o.`order_id`
            SET o.`reopen_count` = COALESCE(h.`fail_count`, 0),
                o.`is_reopen` = CASE WHEN COALESCE(h.`fail_count`, 0) > 0 THEN 1 ELSE 0 END;";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureRequirementBoardTablesAsync(IServiceProvider services)
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

        async Task<bool> ColumnExistsAsync(string tableName, string columnName)
        {
            command.CommandText = $@"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = '{tableName}'
                  AND COLUMN_NAME = '{columnName}';";

            return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        }

        async Task<bool> ConstraintExistsAsync(string constraintName)
        {
            command.CommandText = $@"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND CONSTRAINT_NAME = '{constraintName}';";

            return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        }

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_board_groups` (
              `group_id` int(11) NOT NULL AUTO_INCREMENT,
              `group_name` varchar(150) NOT NULL,
              `sort_order` int(11) NOT NULL DEFAULT 0,
              `is_active` tinyint(1) NOT NULL DEFAULT 1,
              `created_by_user_id` int(11) DEFAULT NULL,
              `created_by_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime DEFAULT current_timestamp(),
              `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`group_id`),
              KEY `idx_requirement_board_groups_active_sort` (`is_active`,`sort_order`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_boards` (
              `board_id` int(11) NOT NULL AUTO_INCREMENT,
              `group_id` int(11) NOT NULL,
              `board_name` varchar(150) NOT NULL,
              `cover_image_path` varchar(500) DEFAULT NULL,
              `cover_color` varchar(20) NOT NULL DEFAULT '#22c7b8',
              `sort_order` int(11) NOT NULL DEFAULT 0,
              `is_active` tinyint(1) NOT NULL DEFAULT 1,
              `created_by_user_id` int(11) DEFAULT NULL,
              `created_by_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime DEFAULT current_timestamp(),
              `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`board_id`),
              KEY `idx_requirement_boards_group_sort` (`group_id`,`sort_order`),
              KEY `idx_requirement_boards_active_sort` (`is_active`,`sort_order`),
              CONSTRAINT `fk_requirement_boards_group`
                FOREIGN KEY (`group_id`) REFERENCES `requirement_board_groups` (`group_id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            INSERT INTO `requirement_board_groups` (`group_name`, `sort_order`)
            SELECT 'Project Boards', 1
            FROM DUAL
            WHERE NOT EXISTS (
                SELECT 1 FROM `requirement_board_groups`
                WHERE `group_name` = 'Project Boards'
            );";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            INSERT INTO `requirement_boards` (`group_id`, `board_name`, `cover_color`, `sort_order`)
            SELECT g.`group_id`, 'Default Project Board', '#22c7b8', 1
            FROM `requirement_board_groups` g
            WHERE g.`group_name` = 'Project Boards'
              AND NOT EXISTS (
                  SELECT 1 FROM `requirement_boards`
                  WHERE `board_name` = 'Default Project Board'
              )
            LIMIT 1;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_board_columns` (
              `column_id` int(11) NOT NULL AUTO_INCREMENT,
              `board_id` int(11) NOT NULL,
              `column_name` varchar(150) NOT NULL,
              `sort_order` int(11) NOT NULL DEFAULT 0,
              `is_active` tinyint(1) NOT NULL DEFAULT 1,
              `created_by_user_id` int(11) DEFAULT NULL,
              `created_by_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime DEFAULT current_timestamp(),
              `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`column_id`),
              KEY `idx_requirement_columns_sort` (`sort_order`),
              KEY `idx_requirement_columns_board_sort` (`board_id`,`sort_order`),
              CONSTRAINT `fk_requirement_columns_board`
                FOREIGN KEY (`board_id`) REFERENCES `requirement_boards` (`board_id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        if (!await ColumnExistsAsync("requirement_board_columns", "board_id"))
        {
            command.CommandText = @"
                ALTER TABLE `requirement_board_columns`
                  ADD COLUMN `board_id` int(11) NULL AFTER `column_id`;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            UPDATE `requirement_board_columns`
            SET `board_id` = (
                SELECT `board_id`
                FROM `requirement_boards`
                WHERE `board_name` = 'Default Project Board'
                ORDER BY `board_id`
                LIMIT 1
            )
            WHERE `board_id` IS NULL OR `board_id` = 0;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            ALTER TABLE `requirement_board_columns`
              MODIFY COLUMN `board_id` int(11) NOT NULL;";
        await command.ExecuteNonQueryAsync();

        if (!await ConstraintExistsAsync("fk_requirement_columns_board"))
        {
            command.CommandText = @"
                ALTER TABLE `requirement_board_columns`
                  ADD CONSTRAINT `fk_requirement_columns_board`
                  FOREIGN KEY (`board_id`) REFERENCES `requirement_boards` (`board_id`)
                  ON DELETE CASCADE;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_cards` (
              `card_id` int(11) NOT NULL AUTO_INCREMENT,
              `column_id` int(11) NOT NULL,
              `title` varchar(255) NOT NULL,
              `detail` text DEFAULT NULL,
              `cover_image_path` varchar(500) DEFAULT NULL,
              `cover_image_name` varchar(255) DEFAULT NULL,
              `sort_order` int(11) NOT NULL DEFAULT 0,
              `is_archived` tinyint(1) NOT NULL DEFAULT 0,
              `created_by_user_id` int(11) DEFAULT NULL,
              `created_by_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime DEFAULT current_timestamp(),
              `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`card_id`),
              KEY `idx_requirement_cards_column_sort` (`column_id`,`sort_order`),
              KEY `idx_requirement_cards_created_by_user` (`created_by_user_id`),
              KEY `idx_requirement_cards_created_by_emp` (`created_by_emp_id`),
              CONSTRAINT `fk_requirement_cards_column`
                FOREIGN KEY (`column_id`) REFERENCES `requirement_board_columns` (`column_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        if (!await ColumnExistsAsync("requirement_cards", "cover_image_path"))
        {
            command.CommandText = @"
                ALTER TABLE `requirement_cards`
                  ADD COLUMN `cover_image_path` varchar(500) DEFAULT NULL AFTER `detail`,
                  ADD COLUMN `cover_image_name` varchar(255) DEFAULT NULL AFTER `cover_image_path`;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_card_attachments` (
              `attachment_id` int(11) NOT NULL AUTO_INCREMENT,
              `card_id` int(11) NOT NULL,
              `file_name` varchar(255) NOT NULL,
              `stored_file_name` varchar(255) NOT NULL,
              `file_path` varchar(500) NOT NULL,
              `content_type` varchar(150) DEFAULT NULL,
              `file_size` bigint DEFAULT 0,
              `uploaded_by_user_id` int(11) DEFAULT NULL,
              `uploaded_by_emp_id` int(11) DEFAULT NULL,
              `uploaded_at` datetime DEFAULT current_timestamp(),
              PRIMARY KEY (`attachment_id`),
              KEY `idx_requirement_attachments_card` (`card_id`),
              KEY `idx_requirement_attachments_user` (`uploaded_by_user_id`),
              KEY `idx_requirement_attachments_emp` (`uploaded_by_emp_id`),
              CONSTRAINT `fk_requirement_attachments_card`
                FOREIGN KEY (`card_id`) REFERENCES `requirement_cards` (`card_id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_board_labels` (
              `label_id` int(11) NOT NULL AUTO_INCREMENT,
              `label_name` varchar(100) NOT NULL,
              `color_hex` varchar(20) NOT NULL DEFAULT '#22c7b8',
              `sort_order` int(11) NOT NULL DEFAULT 0,
              `is_active` tinyint(1) NOT NULL DEFAULT 1,
              `created_by_user_id` int(11) DEFAULT NULL,
              `created_by_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime DEFAULT current_timestamp(),
              `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`label_id`),
              KEY `idx_requirement_labels_active_sort` (`is_active`,`sort_order`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_card_labels` (
              `card_id` int(11) NOT NULL,
              `label_id` int(11) NOT NULL,
              `created_at` datetime DEFAULT current_timestamp(),
              PRIMARY KEY (`card_id`,`label_id`),
              KEY `idx_requirement_card_labels_label` (`label_id`),
              CONSTRAINT `fk_requirement_card_labels_card`
                FOREIGN KEY (`card_id`) REFERENCES `requirement_cards` (`card_id`) ON DELETE CASCADE,
              CONSTRAINT `fk_requirement_card_labels_label`
                FOREIGN KEY (`label_id`) REFERENCES `requirement_board_labels` (`label_id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS `requirement_card_phase_items` (
              `item_id` int(11) NOT NULL AUTO_INCREMENT,
              `card_id` int(11) NOT NULL,
              `phase_name` varchar(500) NOT NULL,
              `phase_type` varchar(20) NOT NULL DEFAULT 'MAIN',
              `phase_order` int(11) NOT NULL DEFAULT 1,
              `period_order` int(11) NOT NULL DEFAULT 1,
              `phase_sort` int(11) NOT NULL DEFAULT 0,
              `phase_status` varchar(50) DEFAULT 'วางแผน',
              `plan_start` date DEFAULT NULL,
              `plan_end` date DEFAULT NULL,
              `period_end_date` date DEFAULT NULL,
              `created_by_user_id` int(11) DEFAULT NULL,
              `created_by_emp_id` int(11) DEFAULT NULL,
              `created_at` datetime DEFAULT current_timestamp(),
              `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
              PRIMARY KEY (`item_id`),
              KEY `idx_requirement_card_phase_card_sort` (`card_id`,`phase_sort`),
              CONSTRAINT `fk_requirement_card_phase_card`
                FOREIGN KEY (`card_id`) REFERENCES `requirement_cards` (`card_id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            INSERT INTO `requirement_board_columns` (`board_id`, `column_name`, `sort_order`)
            SELECT b.`board_id`, 'To Do', 1
            FROM `requirement_boards` b
            WHERE b.`board_name` = 'Default Project Board'
              AND NOT EXISTS (
                  SELECT 1 FROM `requirement_board_columns` c
                  WHERE c.`board_id` = b.`board_id`
                    AND c.`column_name` = 'To Do'
              )
            LIMIT 1;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            INSERT INTO `requirement_board_columns` (`board_id`, `column_name`, `sort_order`)
            SELECT b.`board_id`, 'Complete', 2
            FROM `requirement_boards` b
            WHERE b.`board_name` = 'Default Project Board'
              AND NOT EXISTS (
                  SELECT 1 FROM `requirement_board_columns` c
                  WHERE c.`board_id` = b.`board_id`
                    AND c.`column_name` = 'Complete'
              )
            LIMIT 1;";
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
            ALTER TABLE `ProjectIssues`
              MODIFY COLUMN `IssueStatus` varchar(20) NULL DEFAULT 'OPEN',
              MODIFY COLUMN `DevStatus` varchar(20) NULL DEFAULT 'WIP';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `ProjectIssues`
            SET `IssueStatus` = CASE
                WHEN `IssueStatus` IN ('OPEN', 'PASS', 'FAIL', 'REJECT') THEN `IssueStatus`
                WHEN `IssueStatus` IN ('WAIT_TEST', 'WIP', 'FIXED', 'IN_PROGRESS', 'TODO', 'DOING', 'BLOCK') THEN 'OPEN'
                WHEN `IssueStatus` IN ('DONE', 'CLOSE', 'CLOSED', 'RESOLVED') THEN 'PASS'
                WHEN `IssueStatus` IS NULL OR `IssueStatus` = '' THEN 'OPEN'
                ELSE 'OPEN'
            END;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `ProjectIssues`
            SET `DevStatus` = CASE
                WHEN `DevStatus` = 'FIXED' THEN 'FIXED'
                WHEN `DevStatus` IN ('TODO', 'DOING', 'BLOCK', 'IN_PROGRESS', 'OPEN', 'FAIL', 'PASS', 'REJECT') THEN 'WIP'
                WHEN `DevStatus` IS NULL OR `DevStatus` = '' THEN 'WIP'
                ELSE 'WIP'
            END;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `ProjectIssues`
            SET `DevStatus` = 'FIXED'
            WHERE `IssueStatus` = 'PASS'
              AND `DevStatus` <> 'FIXED';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `ProjectIssues`
            SET `DevStatus` = 'WIP'
            WHERE `IssueStatus` = 'FAIL'
              AND `DevStatus` <> 'WIP';";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            INSERT INTO `ProjectIssueStatusHistories`
                (`IssueId`, `OldStatus`, `NewStatus`, `IsReopen`, `ReopenCount`, `ChangedAt`, `ChangedByEmpId`)
            SELECT
                i.`IssueId`,
                NULL,
                COALESCE(i.`IssueStatus`, 'OPEN'),
                COALESCE(i.`IsReopen`, 0),
                COALESCE(i.`ReopenCount`, 0),
                COALESCE(i.`CreatedAt`, NOW()),
                i.`created_by`
            FROM `ProjectIssues` i
            WHERE NOT EXISTS (
                SELECT 1
                FROM `ProjectIssueStatusHistories` h
                WHERE h.`IssueId` = i.`IssueId`
            );";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            UPDATE `ProjectIssues` i
            LEFT JOIN (
                SELECT `IssueId`, COUNT(*) AS `fail_count`
                FROM `ProjectIssueStatusHistories`
                WHERE `NewStatus` = 'FAIL'
                GROUP BY `IssueId`
            ) h ON h.`IssueId` = i.`IssueId`
            SET i.`ReopenCount` = COALESCE(h.`fail_count`, 0),
                i.`IsReopen` = CASE WHEN COALESCE(h.`fail_count`, 0) > 0 THEN 1 ELSE 0 END;";
        await command.ExecuteNonQueryAsync();

        command.CommandText = @"
            ALTER TABLE `ProjectIssues`
              MODIFY COLUMN `IssueStatus` varchar(20) NOT NULL DEFAULT 'OPEN',
              MODIFY COLUMN `DevStatus` varchar(20) NOT NULL DEFAULT 'WIP';";
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureTestScenarioReadyStatusValuesAsync(IServiceProvider services)
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

        async Task<bool> TableExistsAsync(string tableName)
        {
            command.CommandText = $@"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = '{tableName}';";

            return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        }

        async Task<bool> ColumnExistsAsync(string tableName, string columnName)
        {
            command.CommandText = $@"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = '{tableName}'
                  AND COLUMN_NAME = '{columnName}';";

            return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;
        }

        if (await TableExistsAsync("project_test_scenarios") &&
            await ColumnExistsAsync("project_test_scenarios", "status"))
        {
            command.CommandText = @"
                UPDATE `project_test_scenarios`
                SET `status` = 'READY'
                WHERE UPPER(COALESCE(`status`, '')) = 'DRAFT'
                   OR TRIM(COALESCE(`status`, '')) = '';";
            await command.ExecuteNonQueryAsync();

            command.CommandText = @"
                ALTER TABLE `project_test_scenarios`
                MODIFY COLUMN `status` varchar(20) NOT NULL DEFAULT 'READY';";
            await command.ExecuteNonQueryAsync();
        }

        if (await TableExistsAsync("test_scenario_templates") &&
            await ColumnExistsAsync("test_scenario_templates", "status_default"))
        {
            command.CommandText = @"
                UPDATE `test_scenario_templates`
                SET `status_default` = 'READY'
                WHERE UPPER(COALESCE(`status_default`, '')) = 'DRAFT'
                   OR TRIM(COALESCE(`status_default`, '')) = '';";
            await command.ExecuteNonQueryAsync();

            command.CommandText = @"
                ALTER TABLE `test_scenario_templates`
                MODIFY COLUMN `status_default` varchar(20) NOT NULL DEFAULT 'READY';";
            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task EnsureDevGitHistoryTablesAsync(IServiceProvider services)
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
            FROM information_schema.tables
            WHERE table_schema = DATABASE()
              AND table_name = 'ProjectIssues';";
        var hasProjectIssues = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;

        if (hasProjectIssues)
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS `project_issue_git_histories` (
                  `id` int NOT NULL AUTO_INCREMENT,
                  `issue_id` int NOT NULL,
                  `git_type` varchar(10) NOT NULL,
                  `git_id` varchar(80) NOT NULL,
                  `entry_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  `created_by_emp_id` int NULL,
                  PRIMARY KEY (`id`),
                  KEY `IX_project_issue_git_histories_issue_id` (`issue_id`),
                  KEY `IX_project_issue_git_histories_entry_date` (`entry_date`),
                  KEY `IX_project_issue_git_histories_issue_id_entry_date` (`issue_id`, `entry_date`),
                  CONSTRAINT `FK_project_issue_git_histories_issue`
                    FOREIGN KEY (`issue_id`) REFERENCES `ProjectIssues` (`IssueId`)
                    ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            await command.ExecuteNonQueryAsync();
        }

        command.CommandText = @"
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = DATABASE()
              AND table_name = 'project_support_order';";
        var hasSupportOrders = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0) > 0;

        if (hasSupportOrders)
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS `project_support_order_git_histories` (
                  `id` int NOT NULL AUTO_INCREMENT,
                  `order_id` int NOT NULL,
                  `git_type` varchar(10) NOT NULL,
                  `git_id` varchar(80) NOT NULL,
                  `entry_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  `created_by_emp_id` int NULL,
                  PRIMARY KEY (`id`),
                  KEY `IX_project_support_order_git_histories_order_id` (`order_id`),
                  KEY `IX_project_support_order_git_histories_entry_date` (`entry_date`),
                  KEY `IX_project_support_order_git_histories_order_id_entry_date` (`order_id`, `entry_date`),
                  CONSTRAINT `FK_project_support_order_git_histories_order`
                    FOREIGN KEY (`order_id`) REFERENCES `project_support_order` (`order_id`)
                    ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}

static async Task PrintStatusCleanupSummaryAsync(IServiceProvider services)
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
            SELECT
                (SELECT COUNT(*) FROM `ProjectIssues`
                 WHERE `IssueStatus` NOT IN ('OPEN', 'PASS', 'FAIL', 'REJECT')
                    OR `IssueStatus` IS NULL
                    OR `IssueStatus` = '') AS InvalidIssueStatus,
                (SELECT COUNT(*) FROM `ProjectIssues`
                 WHERE `DevStatus` NOT IN ('WIP', 'FIXED')
                    OR `DevStatus` IS NULL
                    OR `DevStatus` = '') AS InvalidIssueDevStatus,
                (SELECT COUNT(*) FROM `project_support_order`
                 WHERE `status` NOT IN ('OPEN', 'PASS', 'FAIL', 'REJECT')
                    OR `status` IS NULL
                    OR `status` = '') AS InvalidSupportStatus,
                (SELECT COUNT(*) FROM `project_support_order`
                 WHERE `dev_status` NOT IN ('WIP', 'FIXED')
                    OR `dev_status` IS NULL
                    OR `dev_status` = '') AS InvalidSupportDevStatus,
                (SELECT COUNT(*) FROM `project_support_order_status_histories`) AS SupportStatusHistoryRows,
                (SELECT COUNT(*) FROM `project_support_order` o
                 WHERE NOT EXISTS (
                    SELECT 1
                    FROM `project_support_order_status_histories` h
                    WHERE h.`order_id` = o.`order_id`
                 )) AS SupportOrdersWithoutHistory,
                (SELECT COUNT(*) FROM `ProjectIssues` i
                 WHERE NOT EXISTS (
                    SELECT 1
                    FROM `ProjectIssueStatusHistories` h
                    WHERE h.`IssueId` = i.`IssueId`
                 )) AS ProjectIssuesWithoutHistory,
                (SELECT COUNT(*) FROM `ProjectIssues`
                 WHERE `IssueStatus` = 'PASS'
                   AND `DevStatus` <> 'FIXED') AS ProjectIssuesPassDevNotFixed,
                (SELECT COUNT(*) FROM `ProjectIssues`
                 WHERE `IssueStatus` = 'FAIL'
                   AND `DevStatus` <> 'WIP') AS ProjectIssuesFailDevNotWip,
                (SELECT COUNT(*) FROM `project_support_order`
                 WHERE `status` = 'PASS'
                   AND `dev_status` <> 'FIXED') AS SupportOrdersPassDevNotFixed,
                (SELECT COUNT(*) FROM `project_support_order`
                 WHERE `status` = 'FAIL'
                   AND `dev_status` <> 'WIP') AS SupportOrdersFailDevNotWip,
                (SELECT COUNT(*) FROM `ProjectIssues` i
                 WHERE COALESCE(i.`ReopenCount`, 0) <> (
                    SELECT COUNT(*)
                    FROM `ProjectIssueStatusHistories` h
                    WHERE h.`IssueId` = i.`IssueId`
                      AND h.`NewStatus` = 'FAIL'
                 )) AS ProjectIssuesFailCountMismatch,
                (SELECT COUNT(*) FROM `project_support_order` o
                 WHERE COALESCE(o.`reopen_count`, 0) <> (
                    SELECT COUNT(*)
                    FROM `project_support_order_status_histories` h
                    WHERE h.`order_id` = o.`order_id`
                      AND h.`new_status` = 'FAIL'
                 )) AS SupportOrdersFailCountMismatch,
                (SELECT COUNT(*) FROM `ProjectIssues`
                 WHERE COALESCE(`IsReopen`, 0) <> CASE WHEN COALESCE(`ReopenCount`, 0) > 0 THEN 1 ELSE 0 END
                ) AS ProjectIssuesIsReopenMismatch,
                (SELECT COUNT(*) FROM `project_support_order`
                 WHERE COALESCE(`is_reopen`, 0) <> CASE WHEN COALESCE(`reopen_count`, 0) > 0 THEN 1 ELSE 0 END
                ) AS SupportOrdersIsReopenMismatch;";

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            Console.WriteLine($"Invalid ProjectIssues.IssueStatus: {reader.GetInt64(0)}");
            Console.WriteLine($"Invalid ProjectIssues.DevStatus: {reader.GetInt64(1)}");
            Console.WriteLine($"Invalid SupportOrders.Status: {reader.GetInt64(2)}");
            Console.WriteLine($"Invalid SupportOrders.DevStatus: {reader.GetInt64(3)}");
            Console.WriteLine($"SupportOrders status history rows: {reader.GetInt64(4)}");
            Console.WriteLine($"SupportOrders without status history: {reader.GetInt64(5)}");
            Console.WriteLine($"ProjectIssues without status history: {reader.GetInt64(6)}");
            Console.WriteLine($"ProjectIssues PASS but DevStatus not FIXED: {reader.GetInt64(7)}");
            Console.WriteLine($"ProjectIssues FAIL but DevStatus not WIP: {reader.GetInt64(8)}");
            Console.WriteLine($"SupportOrders PASS but DevStatus not FIXED: {reader.GetInt64(9)}");
            Console.WriteLine($"SupportOrders FAIL but DevStatus not WIP: {reader.GetInt64(10)}");
            Console.WriteLine($"ProjectIssues FAIL count mismatch: {reader.GetInt64(11)}");
            Console.WriteLine($"SupportOrders FAIL count mismatch: {reader.GetInt64(12)}");
            Console.WriteLine($"ProjectIssues IsReopen mismatch: {reader.GetInt64(13)}");
            Console.WriteLine($"SupportOrders IsReopen mismatch: {reader.GetInt64(14)}");
        }
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
}
