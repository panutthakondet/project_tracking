using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("requirement_card_attachments")]
    public class RequirementCardAttachment
    {
        [Key]
        [Column("attachment_id")]
        public int AttachmentId { get; set; }

        [Column("card_id")]
        public int CardId { get; set; }

        [Required]
        [StringLength(255)]
        [Column("file_name")]
        public string FileName { get; set; } = "";

        [Required]
        [StringLength(255)]
        [Column("stored_file_name")]
        public string StoredFileName { get; set; } = "";

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

        [Column("uploaded_by_emp_id")]
        public int? UploadedByEmpId { get; set; }

        [Column("uploaded_at")]
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(CardId))]
        public RequirementCard? Card { get; set; }

        [ForeignKey(nameof(UploadedByUserId))]
        public LoginUser? UploadedByUser { get; set; }

        [ForeignKey(nameof(UploadedByEmpId))]
        public Employee? UploadedByEmployee { get; set; }
    }
}
