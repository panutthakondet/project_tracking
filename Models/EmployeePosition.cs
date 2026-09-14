using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("employee_positions")]
    public class EmployeePosition
    {
        [Key]
        [Column("position_id")]
        public int PositionId { get; set; }

        [Required, StringLength(150)]
        [Column("position_name")]
        public string PositionName { get; set; } = string.Empty;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }
}
