using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("meeting_room_objects")]
    public class MeetingRoomObject
    {
        [Key]
        [Column("object_id")]
        public int ObjectId { get; set; }

        [Column("object_key")]
        [StringLength(80)]
        public string ObjectKey { get; set; } = "desk-basic";

        [Column("object_type")]
        [StringLength(30)]
        public string ObjectType { get; set; } = "DESK";

        [Column("title")]
        [StringLength(100)]
        public string Title { get; set; } = "Desk";

        [Column("tone")]
        [StringLength(20)]
        public string Tone { get; set; } = "wood";

        [Column("x")]
        public int X { get; set; }

        [Column("y")]
        public int Y { get; set; }

        [Column("w")]
        public int W { get; set; }

        [Column("h")]
        public int H { get; set; }

        [Column("rotation")]
        public int Rotation { get; set; }

        [Column("is_obstacle")]
        public bool IsObstacle { get; set; } = true;

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
