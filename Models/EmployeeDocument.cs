using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    /// <summary>
    /// ไฟล์เอกสารที่จัดเก็บแยกตามพนักงาน
    /// </summary>
    [Table("employee_documents")]
    public class EmployeeDocument
    {
        [Key]
        [Column("employee_document_id")]
        public int EmployeeDocumentId { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("document_type")]
        [StringLength(20)]
        public string? DocumentType { get; set; }

        [Column("file_name")]
        [StringLength(255)]
        public string? FileName { get; set; }

        [Column("description")]
        [StringLength(2000)]
        public string? Description { get; set; }

        [Column("file_path")]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Column("uploaded_by")]
        [StringLength(100)]
        public string? UploadedBy { get; set; }

        [Column("uploaded_at")]
        public DateTime UploadedAt { get; set; }

        public Employee? Employee { get; set; }
    }
}
