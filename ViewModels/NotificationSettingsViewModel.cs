namespace ProjectTracking.ViewModels
{
    public class NotificationSettingsViewModel
    {
        public List<NotificationSettingItemViewModel> Items { get; set; } = new();

        public int EnabledCount => Items.Count(x => x.IsEnabled);
        public int DisabledCount => Items.Count - EnabledCount;
    }

    public class NotificationSettingItemViewModel
    {
        public string FeatureKey { get; set; } = "";
        public string ConfigKey { get; set; } = "";
        public string Group { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
    }

    public class NotificationSettingInputViewModel
    {
        public string FeatureKey { get; set; } = "";
        public bool IsEnabled { get; set; }
    }

    public class NotificationSettingsPostViewModel
    {
        public List<NotificationSettingInputViewModel> Items { get; set; } = new();
    }
}
