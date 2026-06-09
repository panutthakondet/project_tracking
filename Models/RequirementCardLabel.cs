using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("requirement_card_labels")]
    public class RequirementCardLabel
    {
        [Column("card_id")]
        public int CardId { get; set; }

        [Column("label_id")]
        public int LabelId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(CardId))]
        public RequirementCard? Card { get; set; }

        [ForeignKey(nameof(LabelId))]
        public RequirementBoardLabel? Label { get; set; }
    }
}
