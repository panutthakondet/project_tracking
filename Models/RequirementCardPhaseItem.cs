using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("requirement_card_phase_items")]
    public class RequirementCardPhaseItem
    {
        [Key]
        [Column("item_id")]
        public int ItemId { get; set; }

        [Column("card_id")]
        public int CardId { get; set; }

        [Required]
        [StringLength(500)]
        [Column("phase_name")]
        public string PhaseName { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("phase_type")]
        public string PhaseType { get; set; } = "MAIN";

        [Column("phase_order")]
        public int PhaseOrder { get; set; } = 1;

        [Column("period_order")]
        public int PeriodOrder { get; set; } = 1;

        [Column("phase_sort")]
        public int PhaseSort { get; set; }

        [StringLength(50)]
        [Column("phase_status")]
        public string? PhaseStatus { get; set; } = "กำลังดำเนินการ";

        [Column("plan_start")]
        public DateTime? PlanStart { get; set; }

        [Column("plan_end")]
        public DateTime? PlanEnd { get; set; }

        [Column("period_end_date")]
        public DateTime? PeriodEndDate { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_by_emp_id")]
        public int? CreatedByEmpId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(CardId))]
        public RequirementCard? Card { get; set; }

        [NotMapped]
        public string PhasePeriodLabel => $"ส่วนที่ {PhaseOrder} งวดที่ {PeriodOrder}";
    }
}
