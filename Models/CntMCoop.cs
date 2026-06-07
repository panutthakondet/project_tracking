using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("cnt_m_coop")]
    public class CntMCoop
    {
        [Key]
        [Column("coop_id")]
        public int CoopId { get; set; }

        [Required]
        [StringLength(255)]
        [Column("coop_name")]
        public string CoopName { get; set; } = string.Empty;

        public ICollection<Project> Projects { get; set; } = new List<Project>();
    }
}
