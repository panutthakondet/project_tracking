namespace ProjectTracking.ViewModels
{
    public sealed class AttendanceReportViewModel
    {
        public string FromDate { get; set; } = "";
        public string ToDate { get; set; } = "";
        public string? ValidationMessage { get; set; }
        public int MaximumRangeDays { get; set; }
        public List<AttendanceReportRowViewModel> Rows { get; set; } = new();
    }

    public sealed class AttendanceReportRowViewModel
    {
        public int EmpId { get; set; }
        public string EmpName { get; set; } = "";
        public string? Position { get; set; }
        public DateTime WorkDate { get; set; }
        public decimal? CheckinLat { get; set; }
        public decimal? CheckinLng { get; set; }
        public decimal? CheckinAccuracyM { get; set; }
        public decimal? CheckoutLat { get; set; }
        public decimal? CheckoutLng { get; set; }
        public decimal? CheckoutAccuracyM { get; set; }
        public DateTime? CheckinTime { get; set; }
        public DateTime? CheckoutTime { get; set; }
        public decimal? DistanceKm { get; set; }
    }
}
