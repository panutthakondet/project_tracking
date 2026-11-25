using ProjectTracking.ViewModels;

namespace ProjectTracking.Services;

public static class ExecutiveWorkSchedule
{
    public static bool IsClosed(string? status) => (status ?? "").Trim().ToUpperInvariant()
        is "DONE" or "COMPLETED" or "CLOSED" or "CANCELLED" or "CANCELED" or "SUBMITTED" or "เสร็จสิ้นแล้ว" or "เสร็จแล้ว" or "เสร็จสิ้น" or "ส่งงานแล้ว" or "ยกเลิก";

    public static ExecutiveReportViewModel Create(IEnumerable<ExecutiveWorkItemViewModel> items, DateTime thaiNow,
        int? departmentId, List<DepartmentReportOptionViewModel> options, string username)
    {
        var monday = thaiNow.Date.AddDays(-((7 + (int)thaiNow.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var next = monday.AddDays(7);
        var afterNext = monday.AddDays(14);
        var thaiComparer = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("th-TH"), true);
        var all = items.OrderBy(x => x.CooperativeName, thaiComparer)
            .ThenBy(x => x.PhaseOrder).ThenBy(x => x.PeriodOrder)
            .ThenBy(x => x.EndDate ?? DateTime.MaxValue)
            .ThenBy(x => x.ProjectName, thaiComparer).ThenBy(x => x.Title, thaiComparer)
            .ThenBy(x => x.OwnerName, thaiComparer).ToList();
        // Carry unresolved overdue work into the current week. Undated work stays in the full list.
        var current = all.Where(x => (x.StartDate.HasValue || x.EndDate.HasValue)
            && (x.StartDate?.Date ?? x.EndDate!.Value.Date) < next).ToList();
        var upcoming = all.Where(x => (x.StartDate.HasValue || x.EndDate.HasValue)
            && (x.StartDate?.Date ?? x.EndDate!.Value.Date) < afterNext
            && (x.EndDate?.Date ?? DateTime.MaxValue) >= next).ToList();
        return new ExecutiveReportViewModel {
            WeekStart = monday, GeneratedAt = thaiNow, GeneratedBy = username,
            DepartmentId = departmentId, DepartmentOptions = options,
            ThisWeek = current, NextWeek = upcoming, AllWork = all
        };
    }
}
