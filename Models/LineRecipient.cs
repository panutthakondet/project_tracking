using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("line_recipients")]
    public class LineRecipient
    {
        [Key]
        [Column("line_recipient_id")]
        public int LineRecipientId { get; set; }

        [Column("user_id")]
        public int? UserId { get; set; }

        [Column("emp_id")]
        public int? EmpId { get; set; }

        [Required]
        [StringLength(20)]
        [Column("recipient_type")]
        public string RecipientType { get; set; } = "USER";

        [StringLength(100)]
        [Column("line_user_id")]
        public string? LineUserId { get; set; }

        [StringLength(100)]
        [Column("line_group_id")]
        public string? LineGroupId { get; set; }

        [StringLength(255)]
        [Column("line_display_name")]
        public string? LineDisplayName { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("last_followed_at")]
        public DateTime? LastFollowedAt { get; set; }

        [Column("last_webhook_at")]
        public DateTime? LastWebhookAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(UserId))]
        public LoginUser? User { get; set; }

        [ForeignKey(nameof(EmpId))]
        public Employee? Employee { get; set; }
    }
}
