using System.Globalization;

namespace ProjectTracking.Services.Security;

public static class SecurityDisplayDate
{
    private static readonly CultureInfo Thai = CultureInfo.GetCultureInfo("th-TH");
    // MySQL datetime values lose Kind. Security scan timestamps are stored as UTC.
    public static string Utc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).AddHours(7).ToString("d MMM yyyy HH:mm", Thai) + " น."
        : "—";
    // Issue timestamps and date-only schedules use the existing application-local convention.
    public static string Local(DateTime? value, bool time = false) => value.HasValue
        ? value.Value.ToString(time ? "d MMM yyyy HH:mm" : "d MMM yyyy", Thai) + (time ? " น." : "")
        : "—";
}
