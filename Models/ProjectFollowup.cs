using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_followups")]
    public class ProjectFollowup
    {
        [Key]
        [Column("followup_id")]
        public int FollowupId { get; set; }

        [Column("project_id")]
        public int? ProjectId { get; set; }

        [ForeignKey("ProjectId")]
        public Project? Project { get; set; }

        [Required]
        [StringLength(255)]
        [Column("task_title")]
        public string TaskTitle { get; set; } = "";

        [StringLength(255)]
        [Column("partner_name")]
        public string? PartnerName { get; set; }

        [Column("owner_emp_id")]
        public int? OwnerEmpId { get; set; }

        [ForeignKey("OwnerEmpId")]
        public Employee? Owner { get; set; }

        [Column("status")]
        public string Status { get; set; } = "OPEN";

        [Column("next_followup_date")]
        public DateTime? NextFollowupDate { get; set; }

        [Column("last_contact_date")]
        public DateTime? LastContactDate { get; set; }

        [StringLength(20)]
        [Column("last_contact_type")]
        public string? LastContactType { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}