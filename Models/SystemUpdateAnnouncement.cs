using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("system_update_announcements")]
    public class SystemUpdateAnnouncement
    {
        [Key]
        [Column("update_id")]
        public int UpdateId { get; set; }

        [Column("version")]
        [StringLength(50)]
        public string? Version { get; set; }

        [Required]
        [Column("title")]
        [StringLength(255)]
        public string Title { get; set; } = "";

        [Column("summary")]
        [StringLength(500)]
        public string? Summary { get; set; }

        [Column("details")]
        public string? Details { get; set; }

        [Column("published_at")]
        public DateTime PublishedAt { get; set; } = DateTime.Now;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        public ICollection<SystemUpdateRead>? Reads { get; set; }
    }
}
