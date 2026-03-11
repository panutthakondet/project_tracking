using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_support_fix_images")]
    public class ProjectSupportFixImage
    {
        [Key]
        [Column("image_id")]
        public int ImageId { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        [Column("file_path")]
        public string FilePath { get; set; } = string.Empty;

        [Column("image_type")]
        public string ImageType { get; set; } = string.Empty; // BEFORE / AFTER

        [Column("uploaded_at")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime UploadedAt { get; set; }

        [Column("uploaded_by")]
        public int? UploadedBy { get; set; }

        [ForeignKey("OrderId")]
        public ProjectSupportOrder? Order { get; set; }
    }
}