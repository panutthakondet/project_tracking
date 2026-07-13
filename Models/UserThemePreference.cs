using System;

namespace ProjectTracking.Models
{
    public class UserThemePreference
    {
        public int UserId { get; set; }
        public int ThemeId { get; set; }
        public bool UseCustom { get; set; }
        public string? CustomAccentHex { get; set; }
        public string? CustomSidebarHex { get; set; }
        public string? CustomBodyBgHex { get; set; }
        public string? CustomChartPanelHex { get; set; }
        public decimal FontScale { get; set; } = 1.00m;
        public bool ProfileBallEnabled { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ThemePreset? ThemePreset { get; set; }
        public LoginUser? User { get; set; }
    }
}
