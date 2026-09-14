using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("attendance_holiday")]
    public class AttendanceHoliday
    {
        [Key]
        [Column("holiday_date")]
        public DateTime HolidayDate { get; set; }

        [Column("holiday_name")]
        [MaxLength(200)]
        public string HolidayName { get; set; } = "";

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
