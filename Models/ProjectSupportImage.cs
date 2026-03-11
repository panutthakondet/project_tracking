using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_support_images")]
    public class ProjectSupportImage
    {
        [Key]
        [Column("image_id")]
        public int ImageId { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        [Column("file_name")]
        public string? FileName { get; set; }

        [Column("file_path")]
        public string? FilePath { get; set; }

        [NotMapped]
        public DateTime UploadedAt { get; set; }
    }
}