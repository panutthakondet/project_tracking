using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("mailbox_recipients")]
    public class MailboxRecipient
    {
        [Key]
        [Column("recipient_id")]
        public int RecipientId { get; set; }

        [Column("message_id")]
        public int MessageId { get; set; }

        [Column("recipient_user_id")]
        public int RecipientUserId { get; set; }

        [Column("recipient_emp_id")]
        public int? RecipientEmpId { get; set; }

        [Column("is_read")]
        public bool IsRead { get; set; }

        [Column("read_at")]
        public DateTime? ReadAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(MessageId))]
        public MailboxMessage? Message { get; set; }

        [ForeignKey(nameof(RecipientUserId))]
        public LoginUser? RecipientUser { get; set; }

        [ForeignKey(nameof(RecipientEmpId))]
        public Employee? RecipientEmployee { get; set; }
    }
}
