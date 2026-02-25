namespace ProjectTracking.Models
{
    public class TestTemplateGroup
    {
        public int group_id { get; set; }

        public string group_name { get; set; } = string.Empty;

        public bool is_active { get; set; } = true;

        public DateTime created_at { get; set; } = DateTime.Now;

        public ICollection<TestScenarioTemplate>? Templates { get; set; }
    }
}