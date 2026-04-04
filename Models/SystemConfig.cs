using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("system_config")]
    public class SystemConfig
    {
        [Key]
        [Column("config_key")]
        public string? ConfigKey { get; set; }

        [Column("config_value")]
        public string? ConfigValue { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}