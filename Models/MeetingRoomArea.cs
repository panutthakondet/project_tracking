using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("meeting_room_areas")]
    public class MeetingRoomArea
    {
        [Key]
        [Column("area_id")]
        public int AreaId { get; set; }

        [Column("area_key")]
        [StringLength(80)]
        public string AreaKey { get; set; } = "";

        [Column("title")]
        [StringLength(100)]
        public string Title { get; set; } = "";

        [Column("area_type")]
        [StringLength(30)]
        public string AreaType { get; set; } = "MEETING";

        [Column("tone")]
        [StringLength(20)]
        public string Tone { get; set; } = "teal";

        [Column("x")]
        public int X { get; set; }

        [Column("y")]
        public int Y { get; set; }

        [Column("w")]
        public int W { get; set; }

        [Column("h")]
        public int H { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
