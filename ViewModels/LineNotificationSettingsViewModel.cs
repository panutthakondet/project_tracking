namespace ProjectTracking.ViewModels
{
    public class LineNotificationSettingsViewModel
    {
        public List<LineNotificationSettingItemViewModel> Items { get; set; } = new();

        public int EnabledCount => Items.Count(x => x.IsEnabled);
        public int DisabledCount => Items.Count - EnabledCount;
    }

    public class LineNotificationSettingItemViewModel
    {
        public string FeatureKey { get; set; } = "";
        public string ConfigKey { get; set; } = "";
        public string Group { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
    }

    public class LineNotificationSettingInputViewModel
    {
        public string FeatureKey { get; set; } = "";
        public bool IsEnabled { get; set; }
    }

    public class LineNotificationSettingsPostViewModel
    {
        public List<LineNotificationSettingInputViewModel> Items { get; set; } = new();
    }
}
