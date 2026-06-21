namespace ProjectTracking.ViewModels
{
    public class SystemConfigPageViewModel
    {
        public List<SystemConfigItemViewModel> Items { get; set; } = new();

        public int TotalCount => Items.Count;
    }

    public class SystemConfigItemViewModel
    {
        public string ConfigKey { get; set; } = "";
        public string ConfigValue { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "";
        public DateTime? UpdatedAt { get; set; }
    }

    public class SystemConfigInputViewModel
    {
        public string ConfigKey { get; set; } = "";
        public string ConfigValue { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class SystemConfigPostViewModel
    {
        public List<SystemConfigInputViewModel> Items { get; set; } = new();
    }
}
