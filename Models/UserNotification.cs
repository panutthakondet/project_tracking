using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("user_notifications")]
    public class UserNotification
    {
        [Key]
        [Column("notification_id")]
        public int NotificationId { get; set; }

        [Column("recipient_user_id")]
        public int? RecipientUserId { get; set; }

        [Column("recipient_emp_id")]
        public int? RecipientEmpId { get; set; }

        [Required]
        [StringLength(50)]
        [Column("source_type")]
        public string SourceType { get; set; } = "";

        [Column("source_id")]
        public int SourceId { get; set; }

        [Required]
        [StringLength(255)]
        [Column("title")]
        public string Title { get; set; } = "";

        [Column("message", TypeName = "text")]
        public string? Message { get; set; }

        [StringLength(500)]
        [Column("target_url")]
        public string? TargetUrl { get; set; }

        [Required]
        [StringLength(20)]
        [Column("severity")]
        public string Severity { get; set; } = "WARNING";

        [Column("is_read")]
        public bool IsRead { get; set; }

        [Column("read_at")]
        public DateTime? ReadAt { get; set; }

        [Column("is_resolved")]
        public bool IsResolved { get; set; }

        [Column("resolved_at")]
        public DateTime? ResolvedAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(RecipientUserId))]
        public LoginUser? RecipientUser { get; set; }

        [ForeignKey(nameof(RecipientEmpId))]
        public Employee? RecipientEmployee { get; set; }
    }
}
