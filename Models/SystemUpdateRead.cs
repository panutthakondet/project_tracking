using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("system_update_reads")]
    public class SystemUpdateRead
    {
        [Key]
        [Column("read_id")]
        public int ReadId { get; set; }

        [Column("update_id")]
        public int UpdateId { get; set; }

        [ForeignKey("UpdateId")]
        public SystemUpdateAnnouncement? Update { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("read_at")]
        public DateTime ReadAt { get; set; } = DateTime.Now;
    }
}
