using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("weekly_report_attachments")]
    public class WeeklyReportAttachment
    {
        [Key]
        [Column("attachment_id")]
        public int AttachmentId { get; set; }

        [Column("report_id")]
        public int ReportId { get; set; }

        [Required]
        [StringLength(255)]
        [Column("file_name")]
        public string FileName { get; set; } = "";

        [Required]
        [StringLength(500)]
        [Column("file_path")]
        public string FilePath { get; set; } = "";

        [StringLength(150)]
        [Column("content_type")]
        public string? ContentType { get; set; }

        [Column("file_size")]
        public long FileSize { get; set; }

        [Column("uploaded_by_user_id")]
        public int? UploadedByUserId { get; set; }

        [Column("uploaded_at")]
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(ReportId))]
        public WeeklyReport? Report { get; set; }

        [ForeignKey(nameof(UploadedByUserId))]
        public LoginUser? UploadedByUser { get; set; }
    }
}
