

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_documents")]
    public class ProjectDocument
    {
        [Key]
        [Column("document_id")]
        public int DocumentId { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("document_type")]
        [StringLength(20)]
        public string? DocumentType { get; set; }

        [Column("file_name")]
        [StringLength(255)]
        public string? FileName { get; set; }

        [Column("file_path")]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Column("uploaded_by")]
        [StringLength(100)]
        public string? UploadedBy { get; set; }

        [Column("uploaded_at")]
        public DateTime UploadedAt { get; set; }

        // Navigation
        public Project? Project { get; set; }
    }
}