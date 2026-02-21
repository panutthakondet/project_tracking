using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("meeting_attendees")]
    public class MeetingAttendee
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("meeting_id")]
        public int MeetingId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        // ENUM('pending','accepted','rejected')
        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = "pending";

        [ForeignKey(nameof(MeetingId))]
        public Meeting? Meeting { get; set; }
    }
}