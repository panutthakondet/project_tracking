using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("weekly_reports")]
    public class WeeklyReport
    {
        [Key]
        [Column("report_id")]
        public int ReportId { get; set; }

        [Column("week_start", TypeName = "date")]
        [DataType(DataType.Date)]
        public DateTime? WeekStart { get; set; }

        [Column("week_end", TypeName = "date")]
        [DataType(DataType.Date)]
        public DateTime? WeekEnd { get; set; }

        [Required]
        [StringLength(255)]
        [Column("subject")]
        public string Subject { get; set; } = "";

        [Column("summary", TypeName = "text")]
        public string? Summary { get; set; }

        [Required]
        [StringLength(30)]
        [Column("status")]
        public string Status { get; set; } = "DRAFT";

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_by_emp_id")]
        public int? CreatedByEmpId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [Column("sent_to_pm_at")]
        public DateTime? SentToPmAt { get; set; }

        [Column("sent_to_bdm_at")]
        public DateTime? SentToBdmAt { get; set; }

        [ForeignKey(nameof(CreatedByUserId))]
        public LoginUser? CreatedByUser { get; set; }

        [ForeignKey(nameof(CreatedByEmpId))]
        public Employee? CreatedByEmployee { get; set; }

        public ICollection<WeeklyReportAttachment> Attachments { get; set; } = new List<WeeklyReportAttachment>();
    }
}
