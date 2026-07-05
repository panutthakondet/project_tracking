using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("meeting_room_profiles")]
    public class MeetingRoomProfile
    {
        [Key]
        [Column("user_id")]
        public int UserId { get; set; }

        [Column("status")]
        [StringLength(20)]
        public string Status { get; set; } = "AVAILABLE";

        [Column("display_name")]
        [StringLength(50)]
        public string? DisplayName { get; set; }

        [Column("status_text")]
        [StringLength(120)]
        public string? StatusText { get; set; }

        [Column("character_preset")]
        [StringLength(30)]
        public string CharacterPreset { get; set; } = "human";

        [Column("avatar_color")]
        [StringLength(20)]
        public string AvatarColor { get; set; } = "#3b82f6";

        [Column("skin_tone")]
        [StringLength(20)]
        public string SkinTone { get; set; } = "#f2c19b";

        [Column("hair_style")]
        [StringLength(30)]
        public string HairStyle { get; set; } = "short";

        [Column("hair_color")]
        [StringLength(20)]
        public string HairColor { get; set; } = "#2f3137";

        [Column("facial_hair_style")]
        [StringLength(30)]
        public string FacialHairStyle { get; set; } = "none";

        [Column("top_style")]
        [StringLength(30)]
        public string TopStyle { get; set; } = "shirt";

        [Column("top_color")]
        [StringLength(20)]
        public string TopColor { get; set; } = "#3b82f6";

        [Column("jacket_style")]
        [StringLength(30)]
        public string JacketStyle { get; set; } = "none";

        [Column("jacket_color")]
        [StringLength(20)]
        public string JacketColor { get; set; } = "#111827";

        [Column("bottom_style")]
        [StringLength(30)]
        public string BottomStyle { get; set; } = "pants";

        [Column("bottom_color")]
        [StringLength(20)]
        public string BottomColor { get; set; } = "#1f2937";

        [Column("shoes_style")]
        [StringLength(30)]
        public string ShoesStyle { get; set; } = "sneakers";

        [Column("shoes_color")]
        [StringLength(20)]
        public string ShoesColor { get; set; } = "#e5e7eb";

        [Column("hat_style")]
        [StringLength(30)]
        public string HatStyle { get; set; } = "none";

        [Column("hat_color")]
        [StringLength(20)]
        public string HatColor { get; set; } = "#3b82f6";

        [Column("glasses_style")]
        [StringLength(30)]
        public string GlassesStyle { get; set; } = "none";

        [Column("glasses_color")]
        [StringLength(20)]
        public string GlassesColor { get; set; } = "#111827";

        [Column("other_style")]
        [StringLength(30)]
        public string OtherStyle { get; set; } = "none";

        [Column("other_color")]
        [StringLength(20)]
        public string OtherColor { get; set; } = "#ef4444";

        [Column("desk_x")]
        public int DeskX { get; set; } = 50;

        [Column("desk_y")]
        public int DeskY { get; set; } = 50;

        [Column("current_x")]
        public int? CurrentX { get; set; }

        [Column("current_y")]
        public int? CurrentY { get; set; }

        [Column("home_zone")]
        [StringLength(80)]
        public string HomeZone { get; set; } = "Lobby";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(UserId))]
        public LoginUser? User { get; set; }
    }
}
