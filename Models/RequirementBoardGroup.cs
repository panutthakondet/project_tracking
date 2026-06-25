using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("requirement_board_groups")]
    public class RequirementBoardGroup
    {
        [Key]
        [Column("group_id")]
        public int GroupId { get; set; }

        [Required]
        [StringLength(150)]
        [Column("group_name")]
        public string GroupName { get; set; } = "";

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_by_emp_id")]
        public int? CreatedByEmpId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public ICollection<RequirementBoard> Boards { get; set; } = new List<RequirementBoard>();
    }
}
