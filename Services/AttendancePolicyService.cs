using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;

namespace ProjectTracking.Services
{
    public sealed class AttendancePolicyService
    {
        private readonly AppDbContext _context;

        public AttendancePolicyService(AppDbContext context)
        {
            _context = context;
        }

        public static DateTime ThailandNow
        {
            get
            {
                var zoneId = OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok";
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zoneId));
            }
        }

        public static DateTime ThailandToday => ThailandNow.Date;

        public async Task<AttendancePolicy> GetAsync()
        {
            string[] keys =
            {
                "ATTENDANCE_WORK_START_TIME",
                "ATTENDANCE_LATE_GRACE_MINUTES",
                "ATTENDANCE_TARGET_PERCENT",
                "ATTENDANCE_MAX_CHECKOUT_DISTANCE_KM",
                "WFH_MAX_DISTANCE_KM",
                "ATTENDANCE_MAX_GPS_ACCURACY_M",
                "ATTENDANCE_MAX_REPORT_DAYS"
            };

            var values = await _context.SystemConfigs
                .AsNoTracking()
                .Where(x => keys.Contains(x.ConfigKey))
                .ToDictionaryAsync(x => x.ConfigKey!, x => x.ConfigValue);

            var policy = new AttendancePolicy();
            if (values.TryGetValue("ATTENDANCE_WORK_START_TIME", out var workStart)
                && TimeSpan.TryParse(workStart, CultureInfo.InvariantCulture, out var parsedWorkStart))
                policy.WorkStart = parsedWorkStart;
            if (TryInt(values, "ATTENDANCE_LATE_GRACE_MINUTES", out var grace))
                policy.LateGraceMinutes = Math.Clamp(grace, 0, 180);
            if (TryDecimal(values, "ATTENDANCE_TARGET_PERCENT", out var target))
                policy.TargetPercent = Math.Clamp(target, 1m, 100m);
            if (TryDouble(values, "ATTENDANCE_MAX_CHECKOUT_DISTANCE_KM", out var maxDistance)
                || TryDouble(values, "WFH_MAX_DISTANCE_KM", out maxDistance))
                policy.MaximumCheckoutDistanceKm = Math.Clamp(maxDistance, 0.05, 100);
            if (TryDouble(values, "ATTENDANCE_MAX_GPS_ACCURACY_M", out var maxAccuracy))
                policy.MaximumGpsAccuracyM = Math.Clamp(maxAccuracy, 5, 5000);
            if (TryInt(values, "ATTENDANCE_MAX_REPORT_DAYS", out var maxDays))
                policy.MaximumReportDays = Math.Clamp(maxDays, 1, 1096);

            return policy;
        }

        public async Task<HashSet<DateTime>> GetHolidayDatesAsync(DateTime start, DateTime end)
        {
            return (await _context.AttendanceHolidays
                    .AsNoTracking()
                    .Where(x => x.IsActive && x.HolidayDate >= start.Date && x.HolidayDate <= end.Date)
                    .Select(x => x.HolidayDate)
                    .ToListAsync())
                .Select(x => x.Date)
                .ToHashSet();
        }

        public static bool IsWorkday(DateTime date, IReadOnlySet<DateTime>? holidays = null) =>
            date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
            && (holidays == null || !holidays.Contains(date.Date));

        private static bool TryInt(IReadOnlyDictionary<string, string?> values, string key, out int value)
        {
            value = default;
            return values.TryGetValue(key, out var raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryDecimal(IReadOnlyDictionary<string, string?> values, string key, out decimal value)
        {
            value = default;
            return values.TryGetValue(key, out var raw)
                && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryDouble(IReadOnlyDictionary<string, string?> values, string key, out double value)
        {
            value = default;
            return values.TryGetValue(key, out var raw)
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    public sealed class AttendancePolicy
    {
        public TimeSpan WorkStart { get; set; } = TimeSpan.FromHours(9);
        public int LateGraceMinutes { get; set; }
        public decimal TargetPercent { get; set; } = 95m;
        public double MaximumCheckoutDistanceKm { get; set; } = 5;
        public double MaximumGpsAccuracyM { get; set; } = 200;
        public int MaximumReportDays { get; set; } = 366;
    }
}
