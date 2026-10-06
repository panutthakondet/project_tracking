using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    /// <summary>
    /// Master data for document types used by Project Documents.
    /// New types can be managed directly from project_document_types
    /// without changing upload validation or the UI.
    /// </summary>
    [Table("project_document_types")]
    public class ProjectDocumentType
    {
        [Key]
        [Column("document_type_id")]
        public int DocumentTypeId { get; set; }

        [Required]
        [StringLength(20)]
        [Column("document_type_code")]
        public string DocumentTypeCode { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("document_type_name")]
        public string DocumentTypeName { get; set; } = string.Empty;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
