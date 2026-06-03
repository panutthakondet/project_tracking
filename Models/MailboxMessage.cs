using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("mailbox_messages")]
    public class MailboxMessage
    {
        [Key]
        [Column("message_id")]
        public int MessageId { get; set; }

        [Column("report_id")]
        public int? ReportId { get; set; }

        [Required]
        [StringLength(255)]
        [Column("subject")]
        public string Subject { get; set; } = "";

        [Column("body", TypeName = "text")]
        public string? Body { get; set; }

        [Required]
        [StringLength(50)]
        [Column("message_type")]
        public string MessageType { get; set; } = "GENERAL";

        [Column("sender_user_id")]
        public int? SenderUserId { get; set; }

        [Column("sender_emp_id")]
        public int? SenderEmpId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(ReportId))]
        public WeeklyReport? Report { get; set; }

        [ForeignKey(nameof(SenderUserId))]
        public LoginUser? SenderUser { get; set; }

        [ForeignKey(nameof(SenderEmpId))]
        public Employee? SenderEmployee { get; set; }

        public ICollection<MailboxRecipient> Recipients { get; set; } = new List<MailboxRecipient>();
    }
}
