using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("meeting_room_file_shares")]
    public class MeetingRoomFileShare
    {
        [Key]
        [Column("share_id")]
        public int ShareId { get; set; }

        [Column("area_key")]
        [StringLength(80)]
        public string AreaKey { get; set; } = "";

        [Column("area_title")]
        [StringLength(100)]
        public string AreaTitle { get; set; } = "";

        [Column("original_file_name")]
        [StringLength(255)]
        public string OriginalFileName { get; set; } = "";

        [Column("stored_file_name")]
        [StringLength(120)]
        public string StoredFileName { get; set; } = "";

        [Column("content_type")]
        [StringLength(120)]
        public string ContentType { get; set; } = "application/octet-stream";

        [Column("file_size")]
        public long FileSize { get; set; }

        [Column("file_path")]
        [StringLength(500)]
        public string FilePath { get; set; } = "";

        [Column("uploaded_by_user_id")]
        public int UploadedByUserId { get; set; }

        [Column("uploaded_by_name")]
        [StringLength(100)]
        public string UploadedByName { get; set; } = "";

        [Column("uploaded_at")]
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [ForeignKey(nameof(UploadedByUserId))]
        public LoginUser? UploadedByUser { get; set; }
    }
}
