using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_followup_logs")]
    public class ProjectFollowupLog
    {
        [Key]
        [Column("log_id")]
        public int LogId { get; set; }

        [Column("followup_id")]
        public int FollowupId { get; set; }

        [ForeignKey("FollowupId")]
        public ProjectFollowup? Followup { get; set; }

        [Column("contact_date")]
        public DateTime ContactDate { get; set; }

        [Column("contact_type")]
        public string? ContactType { get; set; }

        [Column("note")]
        public string? Note { get; set; }

        [Column("next_followup_date")]
        public DateTime? NextFollowupDate { get; set; }
    }
}