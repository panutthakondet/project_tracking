using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("telegram_recipients")]
    public class TelegramRecipient
    {
        [Key]
        [Column("telegram_recipient_id")]
        public int TelegramRecipientId { get; set; }

        [Column("user_id")]
        public int? UserId { get; set; }

        [Column("emp_id")]
        public int? EmpId { get; set; }

        [Required]
        [StringLength(20)]
        [Column("recipient_type")]
        public string RecipientType { get; set; } = "USER";

        [StringLength(100)]
        [Column("telegram_user_id")]
        public string? TelegramUserId { get; set; }

        [StringLength(100)]
        [Column("telegram_chat_id")]
        public string? TelegramChatId { get; set; }

        [StringLength(255)]
        [Column("telegram_display_name")]
        public string? TelegramDisplayName { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("last_started_at")]
        public DateTime? LastStartedAt { get; set; }

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
