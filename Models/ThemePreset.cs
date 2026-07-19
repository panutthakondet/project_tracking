using System;
using System.Collections.Generic;

namespace ProjectTracking.Models
{
    public class ThemePreset
    {
        public int ThemeId { get; set; }
        public string ThemeKey { get; set; } = "";
        public string ThemeName { get; set; } = "";
        public bool IsSystem { get; set; } = true;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public string AccentHex { get; set; } = "#1F4889";
        public string AccentDarkHex { get; set; } = "#193B70";
        public string AccentDeepHex { get; set; } = "#163260";
        public string SidebarHex { get; set; } = "#081c42";
        public string SidebarDeepHex { get; set; } = "#031934";
        public string ProfilePanelHex { get; set; } = "#0D2A52";
        public string BodyBgHex { get; set; } = "#eef3f9";
        public string ChartPanelHex { get; set; } = "#041F4E";
        public string MenuPanelHex { get; set; } = "#041F4E";
        public string SurfaceHex { get; set; } = "#ffffff";
        public string TextHex { get; set; } = "#0f172a";
        public string MutedHex { get; set; } = "#64748b";
        public string ContrastHex { get; set; } = "#FFFFFF";
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<UserThemePreference> UserPreferences { get; set; } = new List<UserThemePreference>();
    }
}
