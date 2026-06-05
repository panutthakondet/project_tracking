using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("requirement_cards")]
    public class RequirementCard
    {
        [Key]
        [Column("card_id")]
        public int CardId { get; set; }

        [Column("column_id")]
        public int ColumnId { get; set; }

        [Required]
        [StringLength(255)]
        [Column("title")]
        public string Title { get; set; } = "";

        [Column("detail", TypeName = "text")]
        public string? Detail { get; set; }

        [StringLength(500)]
        [Column("cover_image_path")]
        public string? CoverImagePath { get; set; }

        [StringLength(255)]
        [Column("cover_image_name")]
        public string? CoverImageName { get; set; }

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("is_archived")]
        public bool IsArchived { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_by_emp_id")]
        public int? CreatedByEmpId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(ColumnId))]
        public RequirementBoardColumn? Column { get; set; }

        [ForeignKey(nameof(CreatedByUserId))]
        public LoginUser? CreatedByUser { get; set; }

        [ForeignKey(nameof(CreatedByEmpId))]
        public Employee? CreatedByEmployee { get; set; }

        public ICollection<RequirementCardAttachment> Attachments { get; set; } = new List<RequirementCardAttachment>();
    }
}
