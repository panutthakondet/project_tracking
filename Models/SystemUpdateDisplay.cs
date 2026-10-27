using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("system_update_displays")]
    public class SystemUpdateDisplay
    {
        [Key]
        [Column("display_id")]
        public int DisplayId { get; set; }

        [Column("update_id")]
        public int UpdateId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("displayed_on", TypeName = "date")]
        public DateTime DisplayedOn { get; set; }

        [Column("display_count")]
        public int DisplayCount { get; set; }

        [Column("last_displayed_at")]
        public DateTime LastDisplayedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(UpdateId))]
        public SystemUpdateAnnouncement? Update { get; set; }
    }
}
