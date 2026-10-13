using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    /// <summary>
    /// Master ประเภทเอกสารพนักงาน เพิ่มรายการได้จากตารางโดยไม่ต้องแก้โค้ด
    /// </summary>
    [Table("employee_document_types")]
    public class EmployeeDocumentType
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
