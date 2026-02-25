using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("project_test_scenarios")]
    public class TestScenario
    {
        [Key]
        public int scenario_id { get; set; }

        [Required]
        public int project_id { get; set; }

        [Required]
        public string scenario_code { get; set; } = string.Empty;

        [Required]
        public string title { get; set; } = string.Empty;

        public string? precondition { get; set; }

        [Required]
        public string steps { get; set; } = string.Empty;

        [Required]
        public string expected_result { get; set; } = string.Empty;

        public string priority { get; set; } = "MEDIUM";

        public string status { get; set; } = "DRAFT";

        public string? created_by { get; set; }

        public DateTime created_at { get; set; } = DateTime.Now;

        public DateTime? updated_at { get; set; }
    }
}