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
        public string CharacterPreset { get; set; } = "doraemon";

        [Column("avatar_color")]
        [StringLength(20)]
        public string AvatarColor { get; set; } = "#2d9cff";

        [Column("desk_x")]
        public int DeskX { get; set; } = 50;

        [Column("desk_y")]
        public int DeskY { get; set; } = 50;

        [Column("home_zone")]
        [StringLength(80)]
        public string HomeZone { get; set; } = "Lobby";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(UserId))]
        public LoginUser? User { get; set; }
    }
}
