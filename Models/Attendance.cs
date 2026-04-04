using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("attendance")]
    public class Attendance
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("work_date")]
        public DateTime WorkDate { get; set; }

        [Column("checkin_time")]
        public DateTime? CheckinTime { get; set; }

        [Column("checkin_lat")]
        public decimal? CheckinLat { get; set; }

        [Column("checkin_lng")]
        public decimal? CheckinLng { get; set; }

        [Column("checkout_time")]
        public DateTime? CheckoutTime { get; set; }

        [Column("checkout_lat")]
        public decimal? CheckoutLat { get; set; }

        [Column("checkout_lng")]
        public decimal? CheckoutLng { get; set; }

        [Column("distance_km")]
        public decimal? DistanceKm { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}