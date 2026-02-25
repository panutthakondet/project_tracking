namespace ProjectTracking.Models
{
    public class TestScenarioTemplate
    {
        public int template_id { get; set; }

        public int? group_id { get; set; }
        public TestTemplateGroup? Group { get; set; }
        public string title { get; set; } = string.Empty;
        public string? precondition { get; set; }
        public string steps { get; set; } = string.Empty;
        public string expected_result { get; set; } = string.Empty;

        public string priority_default { get; set; } = "MEDIUM";
        public string status_default { get; set; } = "DRAFT";
        public bool is_active { get; set; } = true;

        public DateTime created_at { get; set; } = DateTime.Now;
        public DateTime? updated_at { get; set; }
    }
}