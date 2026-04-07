namespace ProjectTracking.Models
{
    public class AttendanceCheckDto
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string? Type { get; set; } // checkin / checkout
    }
}