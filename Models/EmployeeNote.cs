using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    /// <summary>
    /// บันทึกข้อความที่จัดเก็บแยกตามพนักงาน
    /// </summary>
    [Table("employee_notes")]
    public class EmployeeNote
    {
        [Key]
        [Column("employee_note_id")]
        public int EmployeeNoteId { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("title")]
        [StringLength(255)]
        public string Title { get; set; } = "บันทึกใหม่";

        [Column("content")]
        public string Content { get; set; } = string.Empty;

        [Column("created_by")]
        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [Column("updated_by")]
        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public Employee? Employee { get; set; }
    }
}
