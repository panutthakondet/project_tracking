using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("requirement_boards")]
    public class RequirementBoard
    {
        [Key]
        [Column("board_id")]
        public int BoardId { get; set; }

        [Column("group_id")]
        public int GroupId { get; set; }

        [Required]
        [StringLength(150)]
        [Column("board_name")]
        public string BoardName { get; set; } = "";

        [StringLength(500)]
        [Column("cover_image_path")]
        public string? CoverImagePath { get; set; }

        [Required]
        [StringLength(20)]
        [Column("cover_color")]
        public string CoverColor { get; set; } = "#22c7b8";

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

        [ForeignKey(nameof(GroupId))]
        public RequirementBoardGroup? Group { get; set; }

        public ICollection<RequirementBoardColumn> Columns { get; set; } = new List<RequirementBoardColumn>();
    }
}
