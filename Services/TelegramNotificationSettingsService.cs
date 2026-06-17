using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Services
{
    public static class TelegramNotificationFeatures
    {
        public const string AutoSend = "Auto.Send";
        public const string MeetingsCreate = "Meetings.Create";
        public const string MeetingsUpdate = "Meetings.Update";
        public const string MeetingsCancel = "Meetings.Cancel";
        public const string MeetingsReminder = "Meetings.Reminder";
        public const string ProjectIssuesCreate = "ProjectIssues.Create";
        public const string ProjectIssuesFixed = "ProjectIssues.Fixed";
        public const string SupportOrdersCreate = "SupportOrders.Create";
        public const string SupportOrdersFixed = "SupportOrders.Fixed";
        public const string LineOverdueManual = "Employees.LineOverdue";
        public const string OverdueAuto = "Overdue.Auto";

        public static readonly IReadOnlyList<LineNotificationFeatureDefinition> All =
            new List<LineNotificationFeatureDefinition>
            {
                new(AutoSend, "Auto", "ส่งออโต้", "เปิด/ปิดการส่ง Telegram อัตโนมัติทั้งหมด"),
                new(MeetingsCreate, "Meetings", "Create Meetings", "ส่ง Telegram เมื่อสร้าง Meeting"),
                new(MeetingsUpdate, "Meetings", "Edit Meetings", "ส่ง Telegram เมื่อแก้ไข Meeting"),
                new(MeetingsCancel, "Meetings", "Cancel Meetings", "ส่ง Telegram เมื่อเปลี่ยนสถานะ Meeting เป็นยกเลิก"),
                new(MeetingsReminder, "Meetings", "Meeting Reminder", "ส่ง Telegram เตือนประชุมล่วงหน้า 3, 2, 1, 0 วัน"),
                new(ProjectIssuesCreate, "ProjectIssues", "Create ProjectIssues", "ส่ง Telegram ถึง BA และผู้รับผิดชอบเมื่อสร้าง Issue"),
                new(ProjectIssuesFixed, "ProjectIssues", "FIXED ProjectIssues", "ส่ง Telegram ถึง BA เมื่อ Dev เปลี่ยน Issue เป็น FIXED"),
                new(SupportOrdersCreate, "SupportOrders", "Create SupportOrders", "ส่ง Telegram ถึง BA และผู้รับผิดชอบเมื่อสร้าง Support"),
                new(SupportOrdersFixed, "SupportOrders", "FIXED SupportOrders", "ส่ง Telegram ถึง BA เมื่อ Dev เปลี่ยน Support เป็น FIXED"),
                new(LineOverdueManual, "LineOverdue", "Manual LineOverdue", "ส่ง Telegram จากหน้า Employees/LineOverdue"),
                new(OverdueAuto, "LineOverdue", "Auto Overdue", "ส่ง Telegram อัตโนมัติสำหรับงานเสี่ยงล่าช้า/งานล่าช้า")
            };

        public static string ConfigKey(string featureKey)
        {
            var normalized = (featureKey ?? "")
                .Trim()
                .Replace(".", "_", StringComparison.Ordinal)
                .Replace("-", "_", StringComparison.Ordinal)
                .ToUpperInvariant();

            return $"TELEGRAM_NOTIFICATION_{normalized}_ENABLED";
        }
    }

    public class TelegramNotificationSettingsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public TelegramNotificationSettingsService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<bool> IsEnabledAsync(
            string featureKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(featureKey))
                return true;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var configKey = TelegramNotificationFeatures.ConfigKey(featureKey);
            var value = await db.SystemConfigs
                .AsNoTracking()
                .Where(x => x.ConfigKey == configKey)
                .Select(x => x.ConfigValue)
                .FirstOrDefaultAsync(cancellationToken);

            return IsEnabledValue(value);
        }

        public async Task<LineNotificationSettingsViewModel> BuildViewModelAsync(
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var configKeys = TelegramNotificationFeatures.All
                .Select(x => TelegramNotificationFeatures.ConfigKey(x.FeatureKey))
                .ToList();

            var values = await db.SystemConfigs
                .AsNoTracking()
                .Where(x => x.ConfigKey != null && configKeys.Contains(x.ConfigKey))
                .ToDictionaryAsync(x => x.ConfigKey!, x => x.ConfigValue, cancellationToken);

            var items = TelegramNotificationFeatures.All
                .Select(def =>
                {
                    var configKey = TelegramNotificationFeatures.ConfigKey(def.FeatureKey);
                    values.TryGetValue(configKey, out var value);

                    return new LineNotificationSettingItemViewModel
                    {
                        FeatureKey = def.FeatureKey,
                        ConfigKey = configKey,
                        Group = def.Group,
                        Label = def.Label,
                        Description = def.Description,
                        IsEnabled = IsEnabledValue(value)
                    };
                })
                .ToList();

            return new LineNotificationSettingsViewModel
            {
                Items = items
            };
        }

        public async Task SaveAsync(
            IEnumerable<LineNotificationSettingInputViewModel> items,
            CancellationToken cancellationToken = default)
        {
            var requested = items
                .Where(x => !string.IsNullOrWhiteSpace(x.FeatureKey))
                .GroupBy(x => x.FeatureKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().IsEnabled, StringComparer.OrdinalIgnoreCase);

            var definitions = TelegramNotificationFeatures.All
                .Where(x => requested.ContainsKey(x.FeatureKey))
                .ToList();

            if (definitions.Count == 0)
                return;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var configKeys = definitions
                .Select(x => TelegramNotificationFeatures.ConfigKey(x.FeatureKey))
                .ToList();

            var existing = await db.SystemConfigs
                .Where(x => x.ConfigKey != null && configKeys.Contains(x.ConfigKey))
                .ToDictionaryAsync(x => x.ConfigKey!, cancellationToken);

            var now = DateTime.Now;
            foreach (var definition in definitions)
            {
                var configKey = TelegramNotificationFeatures.ConfigKey(definition.FeatureKey);
                var enabled = requested[definition.FeatureKey];
                var value = enabled ? "true" : "false";
                var description = $"{definition.Group} - {definition.Label}: {definition.Description}";

                if (existing.TryGetValue(configKey, out var config))
                {
                    config.ConfigValue = value;
                    config.Description = description;
                    config.UpdatedAt = now;
                }
                else
                {
                    db.SystemConfigs.Add(new SystemConfig
                    {
                        ConfigKey = configKey,
                        ConfigValue = value,
                        Description = description,
                        UpdatedAt = now
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        private static bool IsEnabledValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            var normalized = value.Trim();
            if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("0", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("off", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("no", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
    }
}
