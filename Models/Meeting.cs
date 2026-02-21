using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("meetings")]
    public class Meeting
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("title")]
        [MaxLength(255)]
        public string Title { get; set; } = "";

        [Column("description")]
        public string? Description { get; set; }

        // DATE in MySQL
        [Column("meeting_date")]
        public DateTime MeetingDate { get; set; }

        // TIME in MySQL
        [Column("start_time")]
        public TimeSpan StartTime { get; set; }

        [Column("end_time")]
        public TimeSpan EndTime { get; set; }

        [Column("location")]
        [MaxLength(255)]
        public string? Location { get; set; }

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("project_id")]
        public int? ProjectId { get; set; }

        public ICollection<MeetingAttendee> Attendees { get; set; } = new List<MeetingAttendee>();
    }
}