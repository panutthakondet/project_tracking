using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models;

[Table("field_service_attachments")]
public class FieldServiceAttachment
{
    [Key, Column("attachment_id")]
    public int AttachmentId { get; set; }
    [Column("visit_id")]
    public int VisitId { get; set; }
    [Required, StringLength(255), Column("file_name")]
    public string FileName { get; set; } = string.Empty;
    [Required, StringLength(500), Column("file_path")]
    public string FilePath { get; set; } = string.Empty;
    [StringLength(150), Column("content_type")]
    public string? ContentType { get; set; }
    [Column("file_size")]
    public long FileSize { get; set; }
    [Column("uploaded_by")]
    public int? UploadedBy { get; set; }
    [Column("uploaded_at")]
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public FieldServiceVisit? Visit { get; set; }
}
