using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_support_order")]
    public class ProjectSupportOrder
    {
        [Key]
        [Column("order_id")]
        public int OrderId { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [ForeignKey("ProjectId")]
        public Project? Project { get; set; }

        [Column("order_title")]
        public string? OrderTitle { get; set; }

        [Column("order_detail")]
        public string? OrderDetail { get; set; }

        [Column("priority")]
        public string? Priority { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("dev_status")]
        public string? DevStatus { get; set; }

        [Column("due_date")]
        public DateTime? DueDate { get; set; }

        [Column("assign_to")]
        public int? AssignTo { get; set; }

        [ForeignKey("AssignTo")]
        public Employee? Employee { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [NotMapped]
        public List<ProjectSupportImage>? Images { get; set; }

        public ICollection<ProjectSupportFixImage>? FixImages { get; set; }
    }
}