using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("notification_send_logs")]
    public class NotificationSendLog
    {
        [Key]
        [Column("log_id")]
        public long LogId { get; set; }

        [Column("channel")]
        public string Channel { get; set; } = "";

        [Column("recipient_emp_id")]
        public int? RecipientEmpId { get; set; }

        [Column("recipient_address")]
        public string? RecipientAddress { get; set; }

        [Column("title")]
        public string Title { get; set; } = "";

        [Column("message")]
        public string? Message { get; set; }

        [Column("target_url")]
        public string? TargetUrl { get; set; }

        [Column("sent_at")]
        public DateTime SentAt { get; set; }
    }
}
