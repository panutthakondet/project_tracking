using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    public class ProjectSupportOrderGitHistory
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("order_id")]
        public int OrderId { get; set; }

        [Required]
        [StringLength(10)]
        [Column("git_type", TypeName = "varchar(10)")]
        public string GitType { get; set; } = "GITHUB";

        [Required]
        [StringLength(80)]
        [Column("git_id", TypeName = "varchar(80)")]
        public string GitId { get; set; } = "";

        [Column("entry_date", TypeName = "datetime")]
        public DateTime EntryDate { get; set; } = DateTime.Now;

        [Column("created_by_emp_id")]
        public int? CreatedByEmpId { get; set; }

        [ForeignKey(nameof(OrderId))]
        public ProjectSupportOrder? Order { get; set; }
    }
}
