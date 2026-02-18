using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("email_send_log")]
    public class EmailSendLog
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("mail_type")]
        public string MailType { get; set; } = "";

        [Column("sent_date")]
        public DateTime SentDate { get; set; }

        [Column("sent_at")]
        public DateTime SentAt { get; set; }
    }
}