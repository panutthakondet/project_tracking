using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("requirement_board_labels")]
    public class RequirementBoardLabel
    {
        [Key]
        [Column("label_id")]
        public int LabelId { get; set; }

        [Required]
        [StringLength(100)]
        [Column("label_name")]
        public string LabelName { get; set; } = "";

        [Required]
        [StringLength(20)]
        [Column("color_hex")]
        public string ColorHex { get; set; } = "#22c7b8";

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

        public ICollection<RequirementCardLabel> CardLabels { get; set; } = new List<RequirementCardLabel>();
    }
}
