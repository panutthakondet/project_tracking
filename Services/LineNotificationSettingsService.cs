using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Services
{
    public static class LineNotificationFeatures
    {
        public const string MeetingsAuto = "Meetings.Auto";
        public const string MeetingsCreate = "Meetings.Create";
        public const string MeetingsUpdate = "Meetings.Update";
        public const string MeetingsCancel = "Meetings.Cancel";
        public const string MeetingsReminder = "Meetings.Reminder";
        public const string PhaseAssignsCreate = "PhaseAssigns.Create";
        public const string ProjectIssuesCreate = "ProjectIssues.Create";
        public const string ProjectIssuesFixed = "ProjectIssues.Fixed";
        public const string ProjectIssuesBaResult = "ProjectIssues.BaResult";
        public const string SupportOrdersCreate = "SupportOrders.Create";
        public const string SupportOrdersFixed = "SupportOrders.Fixed";
        public const string SupportOrdersBaResult = "SupportOrders.BaResult";
        public const string FollowupsCreate = "Followups.Create";
        public const string FollowupsAck = "Followups.Ack";
        public const string FollowupsOwnerUpdate = "Followups.OwnerUpdate";
        public const string LineOverdueManual = "Employees.LineOverdue";
        public const string OverdueAuto = "Overdue.Auto";

        public static readonly IReadOnlyList<LineNotificationFeatureDefinition> All =
            new List<LineNotificationFeatureDefinition>
            {
                new(MeetingsAuto, "Auto", "Auto Meetings", "ส่ง LINE อัตโนมัติสำหรับเตือนประชุมล่วงหน้า"),
                new(MeetingsCreate, "Meetings", "Create Meetings", "ส่ง LINE เมื่อสร้าง Meeting"),
                new(MeetingsUpdate, "Meetings", "Edit Meetings", "ส่ง LINE เมื่อแก้ไข Meeting"),
                new(MeetingsCancel, "Meetings", "Cancel Meetings", "ส่ง LINE เมื่อเปลี่ยนสถานะ Meeting เป็นยกเลิก"),
                new(MeetingsReminder, "Meetings", "Meeting Reminder", "ส่ง LINE เตือนประชุมล่วงหน้า 3, 2, 1, 0 วัน"),
                new(PhaseAssignsCreate, "PhaseAssigns", "Create PhaseAssigns", "ส่ง LINE ถึง BA และผู้รับผิดชอบเมื่อสร้าง Assign"),
                new(ProjectIssuesCreate, "ProjectIssues", "Create ProjectIssues", "ส่ง LINE ถึง BA และผู้รับผิดชอบเมื่อสร้าง Issue"),
                new(ProjectIssuesFixed, "ProjectIssues", "FIXED ProjectIssues", "ส่ง LINE ถึง BA เมื่อ Dev เปลี่ยน Issue เป็น FIXED"),
                new(ProjectIssuesBaResult, "ProjectIssues", "BA Result ProjectIssues", "ส่ง LINE ถึงผู้รับผิดชอบเมื่อ BA เปลี่ยน Issue เป็น PASS/FAIL/REJECT"),
                new(SupportOrdersCreate, "SupportOrders", "Create SupportOrders", "ส่ง LINE ถึง BA และผู้รับผิดชอบเมื่อสร้าง Support"),
                new(SupportOrdersFixed, "SupportOrders", "FIXED SupportOrders", "ส่ง LINE ถึง BA เมื่อ Dev เปลี่ยน Support เป็น FIXED"),
                new(SupportOrdersBaResult, "SupportOrders", "BA Result SupportOrders", "ส่ง LINE ถึงผู้รับผิดชอบเมื่อ BA เปลี่ยน Support เป็น PASS/FAIL/REJECT"),
                new(FollowupsCreate, "Followups", "Create Followups", "ส่ง LINE ถึง Owner เมื่อสร้าง Follow-up"),
                new(FollowupsAck, "Followups", "ACK Followups", "ส่ง LINE ถึง Owner เมื่อผู้สั่งงานเปลี่ยน Follow-up เป็น ACK"),
                new(FollowupsOwnerUpdate, "Followups", "Owner Update Followups", "ส่ง LINE ถึงผู้สั่งงานเมื่อ Owner บันทึก Log หรือ Done"),
                new(LineOverdueManual, "LineOverdue", "Manual LineOverdue", "ส่ง LINE จากหน้า Employees/LineOverdue"),
                new(OverdueAuto, "Auto", "Auto Overdue", "ส่ง LINE อัตโนมัติสำหรับงานเสี่ยงล่าช้า/งานล่าช้า")
            };

        public static string ConfigKey(string featureKey)
        {
            var normalized = (featureKey ?? "")
                .Trim()
                .Replace(".", "_", StringComparison.Ordinal)
                .Replace("-", "_", StringComparison.Ordinal)
                .ToUpperInvariant();

            return $"LINE_NOTIFICATION_{normalized}_ENABLED";
        }
    }

    public sealed record LineNotificationFeatureDefinition(
        string FeatureKey,
        string Group,
        string Label,
        string Description);

    public class LineNotificationSettingsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public LineNotificationSettingsService(IDbContextFactory<AppDbContext> dbFactory)
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
            var configKey = LineNotificationFeatures.ConfigKey(featureKey);
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
            var configKeys = LineNotificationFeatures.All
                .Select(x => LineNotificationFeatures.ConfigKey(x.FeatureKey))
                .ToList();

            var values = await db.SystemConfigs
                .AsNoTracking()
                .Where(x => x.ConfigKey != null && configKeys.Contains(x.ConfigKey))
                .ToDictionaryAsync(x => x.ConfigKey!, x => x.ConfigValue, cancellationToken);

            var items = LineNotificationFeatures.All
                .Select(def =>
                {
                    var configKey = LineNotificationFeatures.ConfigKey(def.FeatureKey);
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

            var definitions = LineNotificationFeatures.All
                .Where(x => requested.ContainsKey(x.FeatureKey))
                .ToList();

            if (definitions.Count == 0)
                return;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var configKeys = definitions
                .Select(x => LineNotificationFeatures.ConfigKey(x.FeatureKey))
                .ToList();

            var existing = await db.SystemConfigs
                .Where(x => x.ConfigKey != null && configKeys.Contains(x.ConfigKey))
                .ToDictionaryAsync(x => x.ConfigKey!, cancellationToken);

            var now = DateTime.Now;
            foreach (var definition in definitions)
            {
                var configKey = LineNotificationFeatures.ConfigKey(definition.FeatureKey);
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
