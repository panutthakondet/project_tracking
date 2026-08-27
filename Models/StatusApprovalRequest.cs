using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("status_approval_requests")]
    public class StatusApprovalRequest
    {
        [Key]
        [Column("request_id")]
        public int RequestId { get; set; }

        [Column("target_type")]
        [StringLength(30)]
        public string TargetType { get; set; } = "";

        [Column("target_id")]
        public int TargetId { get; set; }

        [Column("project_id")]
        public int? ProjectId { get; set; }

        [Column("project_name")]
        [StringLength(255)]
        public string? ProjectName { get; set; }

        [Column("target_title")]
        [StringLength(500)]
        public string? TargetTitle { get; set; }

        [Column("current_status")]
        [StringLength(100)]
        public string? CurrentStatus { get; set; }

        [Column("requested_status")]
        [StringLength(100)]
        public string RequestedStatus { get; set; } = "";

        [Column("request_status")]
        [StringLength(20)]
        public string RequestStatus { get; set; } = "PENDING";

        [Column("request_note")]
        [StringLength(1000)]
        public string? RequestNote { get; set; }

        [Column("requested_by_user_id")]
        public int? RequestedByUserId { get; set; }

        [Column("requested_by_emp_id")]
        public int? RequestedByEmpId { get; set; }

        [Column("requested_at")]
        public DateTime RequestedAt { get; set; } = DateTime.Now;

        [Column("reviewed_by_user_id")]
        public int? ReviewedByUserId { get; set; }

        [Column("reviewed_by_emp_id")]
        public int? ReviewedByEmpId { get; set; }

        [Column("reviewed_at")]
        public DateTime? ReviewedAt { get; set; }

        [Column("review_note")]
        [StringLength(1000)]
        public string? ReviewNote { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
