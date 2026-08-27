namespace ProjectTracking.ViewModels
{
    public class HomeDashboardViewModel
    {
        public string Username { get; set; } = "ผู้ใช้งาน";

        public int TotalProjectCount { get; set; }
        public int MeetingsTodayCount { get; set; }
        public int OpenIssueCount { get; set; }
        public int ActiveMemberCount { get; set; }
        public int OverdueTaskCount { get; set; }

        public string TotalProjectsNote { get; set; } = "ข้อมูลทั้งหมดในระบบ";
        public string MeetingsTodayNote { get; set; } = "รายการประชุมวันนี้";
        public string OpenIssuesNote { get; set; } = "เลยกำหนด Plan";
        public string ActiveMembersNote { get; set; } = "พนักงานสถานะ ACTIVE";
        public string OverdueTasksNote { get; set; } = "เลยกำหนด Plan";

        public List<HomeDashboardMetric> ProjectStatusMetrics { get; set; } = new();
        public string ProjectStatusDonut { get; set; } = "conic-gradient(#263450 0 100%)";

        public List<HomeDashboardMetric> PhaseTypeMetrics { get; set; } = new();
        public int PhaseTypeTotal { get; set; }
        public string PhaseTypeDonut { get; set; } = "conic-gradient(#263450 0 100%)";

        public List<HomeDashboardMetric> IssueMetrics { get; set; } = new();
        public int IssueTotal { get; set; }
        public string IssueDonut { get; set; } = "conic-gradient(#263450 0 100%)";
        public List<HomeDashboardMetric> SupportMetrics { get; set; } = new();
        public int SupportTotal { get; set; }
        public string SupportDonut { get; set; } = "conic-gradient(#263450 0 100%)";

        public List<HomeDashboardMetric> LineOverdueMetrics { get; set; } = new();
        public int LineOverdueTotal { get; set; }
        public int LineOverdueProjectCount { get; set; }
        public string LineOverdueDonut { get; set; } = "conic-gradient(#263450 0 100%)";
        public int LineOverdueLinkedCount { get; set; }
        public int LineOverdueMissingLineCount { get; set; }

        public List<HomeDashboardChartSeries> ProjectOverviewSeries { get; set; } = new();
        public List<HomeDashboardMonthPoint> ProjectOverviewMonths { get; set; } = new();
        public HomeDashboardMonthPoint? ProjectOverviewTooltip { get; set; }
        public List<HomeDashboardProjectOverviewItem> ProjectOverviewProjects { get; set; } = new();
        public List<HomeDashboardProjectDepartmentOption> ProjectOverviewDepartments { get; set; } = new();
        public string SelectedDashboardDepartment { get; set; } = "all";
        public string SelectedDashboardDepartmentName { get; set; } = "ทุกฝ่าย";

        public List<HomeDashboardProjectProgress> TopProjectProgress { get; set; } = new();
        public List<HomeDashboardActivity> RecentActivities { get; set; } = new();
        public List<HomeDashboardMeeting> TodayMeetings { get; set; } = new();
        public List<HomeDashboardMeetingGroupOption> MeetingCalendarGroups { get; set; } = new();
        public string SelectedMeetingGroup { get; set; } = "all";
        public int FieldServiceTodayCount { get; set; }
        public int FieldServicePlannedCount { get; set; }
        public int FieldServiceInProgressCount { get; set; }
        public int FieldServiceCompletedMonthCount { get; set; }
        public int FieldServiceTotalCount { get; set; }
        public string FieldServiceStatusDonut { get; set; } = "conic-gradient(#263450 0 100%)";
        public List<HomeDashboardMetric> FieldServiceStatusMetrics { get; set; } = new();
        public string FieldServiceScopeText { get; set; } = "งานเข้าไซต์ของคุณ";
        public List<HomeDashboardFieldServiceItem> UpcomingFieldServiceVisits { get; set; } = new();
        public List<HomeDashboardTaskPeriod> YearlyTasks { get; set; } = new();
        public int YearlyTaskAxisMax { get; set; } = 4;
        public List<HomeDashboardWatchProject> WatchProjects { get; set; } = new();
        public List<HomeDashboardWorkload> TeamWorkload { get; set; } = new();
        public List<ProjectTaskOverviewMember> TaskOverview { get; set; } = new();
        public int OpenIssueSupportCount { get; set; }
        public List<HomeDashboardOpenWorkItem> OpenIssueSupportItems { get; set; } = new();

        public decimal MonthWorkHours { get; set; }
        public decimal ClosedWorkHours { get; set; }
        public decimal OpenWorkHours { get; set; }
        public int PendingCheckoutCount { get; set; }
        public int TodayCheckinCount { get; set; }
        public int TodayCheckoutCount { get; set; }
        public int TodayMissingCheckinCount { get; set; }
        public int MonthAttendanceDays { get; set; }
        public decimal AverageHoursPerDay { get; set; }
        public int LongShiftCount { get; set; }
        public int LongDistanceCount { get; set; }
        public List<string> PendingCheckoutNames { get; set; } = new();
        public string TimeTrackingDonut { get; set; } = "conic-gradient(#263450 0 100%)";
        public string WorkHourTrendText { get; set; } = "ข้อมูลเดือนนี้จาก attendance";
        public string WorkHourTrendClass { get; set; } = "neutral";
        public decimal TimeTargetHours { get; set; }
        public decimal TimeTargetProgressPercent { get; set; }
        public int ActiveEmployeeCount { get; set; }
        public int TodayOnTimeCount { get; set; }
        public int TodayLateCount { get; set; }
        public int MonthLateCount { get; set; }
        public int MonthIncompleteCheckoutCount { get; set; }
        public int YearLateCount { get; set; }
        public int MonthRecordedEmployeeDays { get; set; }
        public int MonthExpectedEmployeeDays { get; set; }
        public decimal TodayAttendanceRate { get; set; }
        public decimal MonthAttendanceRate { get; set; }
        public decimal MonthPunctualityRate { get; set; }
        public decimal YearAttendanceRate { get; set; }
        public decimal AttendanceTargetPercent { get; set; } = 95m;
        public string AttendancePolicyText { get; set; } = "ตรงเวลาไม่เกิน 09:15 น.";
        public string AttendanceTrendText { get; set; } = "ยังไม่มีข้อมูลเดือนก่อน";
        public string AttendanceTrendClass { get; set; } = "neutral";
        public string AttendanceDonut { get; set; } = "conic-gradient(#263450 0 100%)";
        public List<HomeDashboardTimeTrendDay> TimeTrendDays { get; set; } = new();
        public List<HomeDashboardTimeHeatDay> TimeHeatmapDays { get; set; } = new();
    }

    public class HomeDashboardMetric
    {
        public string StatusCode { get; set; } = "";
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public decimal Percent { get; set; }
        public string Color { get; set; } = "blue";
        public string HexColor { get; set; } = "var(--pt-chart-primary, #1688f5)";

        public string CountPercentText => $"{Count} ({Percent:0.#}%)";
    }

    public class HomeDashboardTimeTrendDay
    {
        public string Label { get; set; } = "";
        public decimal Hours { get; set; }
        public decimal AttendanceRate { get; set; }
        public int PresentCount { get; set; }
        public int ExpectedCount { get; set; }
        public int Percent { get; set; }
        public string Tone { get; set; } = "empty";
        public bool IsWorkday { get; set; }
    }

    public class HomeDashboardTimeHeatDay
    {
        public int Day { get; set; }
        public string Label { get; set; } = "";
        public decimal Hours { get; set; }
        public decimal AttendanceRate { get; set; }
        public int PresentCount { get; set; }
        public int ExpectedCount { get; set; }
        public string Tone { get; set; } = "empty";
        public bool IsToday { get; set; }
        public bool IsWorkday { get; set; }
    }

    public class HomeDashboardChartSeries
    {
        public string Name { get; set; } = "";
        public string Color { get; set; } = "blue";
        public string Points { get; set; } = "";
    }

    public class HomeDashboardMonthPoint
    {
        public string Label { get; set; } = "";
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Pending { get; set; }
    }

    public class HomeDashboardProjectProgress
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Color { get; set; } = "green";
    }

    public class HomeDashboardProjectOverviewItem
    {
        public int ProjectId { get; set; }
        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "ยังไม่กำหนดฝ่าย";
        public string ProjectName { get; set; } = "";
        public string StatusCode { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string StatusColor { get; set; } = "blue";
        public string StartText { get; set; } = "-";
        public string EndText { get; set; } = "-";
    }

    public class HomeDashboardProjectDepartmentOption
    {
        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "";
    }

    public class HomeDashboardActivity
    {
        public string Actor { get; set; } = "";
        public string Detail { get; set; } = "";
        public string OwnerText { get; set; } = "";
        public string TimeText { get; set; } = "";
        public string Color { get; set; } = "blue";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public string? Url { get; set; }
    }

    public class HomeDashboardMeeting
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string GroupName { get; set; } = "";
        public string TimeText { get; set; } = "";
        public string TimeColor { get; set; } = "blue";
        public int AttendeeCount { get; set; }
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
    }

    public class HomeDashboardMeetingGroupOption
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; } = "";
    }

    public class HomeDashboardFieldServiceItem
    {
        public int VisitId { get; set; }
        public string Title { get; set; } = "";
        public string CoopName { get; set; } = "-";
        public string DateText { get; set; } = "-";
        public string AssigneeText { get; set; } = "ยังไม่กำหนด";
        public string StatusText { get; set; } = "";
        public string StatusColor { get; set; } = "blue";
    }

    public class HomeDashboardTaskPeriod
    {
        public string PeriodLabel { get; set; } = "";
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Pending { get; set; }
        public int CompletedHeight { get; set; }
        public int InProgressHeight { get; set; }
        public int PendingHeight { get; set; }
    }

    public class HomeDashboardWatchProject
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public string RiskLevel { get; set; } = "";
        public string RiskColor { get; set; } = "orange";
        public string DueText { get; set; } = "";
        public int RiskScore { get; set; }
        public int Progress { get; set; }
        public List<string> Reasons { get; set; } = new();
    }

    public class HomeDashboardWorkload
    {
        public string Name { get; set; } = "";
        public string DepartmentName { get; set; } = "ยังไม่กำหนดฝ่าย";
        public string Position { get; set; } = "ยังไม่กำหนดตำแหน่ง";
        public int Value { get; set; }
        public int ActiveTaskCount { get; set; }
        public string Color { get; set; } = "blue";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
    }

    public class HomeDashboardOpenWorkItem
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string ProjectName { get; set; } = "-";
        public string OwnerName { get; set; } = "-";
        public string DueText { get; set; } = "-";
        public string Url { get; set; } = "#";
        public string Color { get; set; } = "orange";
    }
}
