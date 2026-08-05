namespace ProjectTracking.Models
{
    public class TestTemplateGroupControl
    {
        public int control_id { get; set; }
        public string control_name { get; set; } = string.Empty;
        public int sort_order { get; set; }
        public bool is_active { get; set; } = true;
        public DateTime created_at { get; set; } = DateTime.Now;
        public ICollection<TestTemplateGroup> Groups { get; set; } = new List<TestTemplateGroup>();
    }
}
