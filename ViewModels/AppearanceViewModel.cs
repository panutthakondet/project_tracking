using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectTracking.ViewModels
{
    public class AppearanceViewModel
    {
        public List<ThemePresetOptionViewModel> Presets { get; set; } = new();
        public int ThemeId { get; set; }
        public bool UseCustom { get; set; }

        [Display(Name = "Accent")]
        public string CustomAccentHex { get; set; } = "#14b8a6";

        [Display(Name = "Sidebar")]
        public string CustomSidebarHex { get; set; } = "#081c42";

        [Display(Name = "Background")]
        public string CustomBodyBgHex { get; set; } = "#eef3f9";

        [Display(Name = "Chart/List Background")]
        public string CustomChartPanelHex { get; set; } = "#081c42";

        [Display(Name = "Menu Background")]
        public string CustomMenuPanelHex { get; set; } = "#081c42";

        [Range(0.90, 1.15)]
        public decimal FontScale { get; set; } = 1.00m;
        public bool ProfileBallEnabled { get; set; }
        [StringLength(24)]
        public string DinoName { get; set; } = "Dino";
        public string DinoColorHex { get; set; } = "#FFFFFF";
        public string DinoFoodColorHex { get; set; } = "#45D6C6";
        public ResolvedThemeViewModel EffectiveTheme { get; set; } = new();
    }

    public class AppearancePostViewModel
    {
        public int ThemeId { get; set; }
        public bool UseCustom { get; set; }
        public string? CustomAccentHex { get; set; }
        public string? CustomSidebarHex { get; set; }
        public string? CustomBodyBgHex { get; set; }
        public string? CustomChartPanelHex { get; set; }
        public string? CustomMenuPanelHex { get; set; }
        public decimal FontScale { get; set; } = 1.00m;
        public bool ProfileBallEnabled { get; set; }
        public string? DinoName { get; set; }
        public string? DinoColorHex { get; set; }
        public string? DinoFoodColorHex { get; set; }
    }

    public class ThemePresetOptionViewModel
    {
        public int ThemeId { get; set; }
        public string ThemeKey { get; set; } = "";
        public string ThemeName { get; set; } = "";
        public bool IsDefault { get; set; }
        public string AccentHex { get; set; } = "#14b8a6";
        public string AccentDarkHex { get; set; } = "#0f766e";
        public string SidebarHex { get; set; } = "#081c42";
        public string BodyBgHex { get; set; } = "#eef3f9";
        public string ChartPanelHex { get; set; } = "#081c42";
        public string MenuPanelHex { get; set; } = "#081c42";
        public string TextHex { get; set; } = "#0f172a";
        public string ContrastHex { get; set; } = "#062b2f";
    }

    public class ResolvedThemeViewModel
    {
        public string AccentHex { get; set; } = "#14b8a6";
        public string AccentDarkHex { get; set; } = "#0f766e";
        public string AccentDeepHex { get; set; } = "#0d5f59";
        public string AccentSoftRgba { get; set; } = "rgba(20, 184, 166, .18)";
        public string AccentGlowRgba { get; set; } = "rgba(20, 184, 166, .30)";
        public string SidebarHex { get; set; } = "#081c42";
        public string SidebarDeepHex { get; set; } = "#031934";
        public string BodyBgHex { get; set; } = "#eef3f9";
        public string ChartPanelHex { get; set; } = "#081c42";
        public string ChartPanelDeepHex { get; set; } = "#031934";
        public string ChartPanelContrastHex { get; set; } = "#ffffff";
        public string ChartPanelContrastMutedRgba { get; set; } = "rgba(255, 255, 255, .76)";
        public string MenuPanelHex { get; set; } = "#081c42";
        public string MenuPanelDeepHex { get; set; } = "#031934";
        public string MenuPanelContrastHex { get; set; } = "#ffffff";
        public string MenuPanelContrastMutedRgba { get; set; } = "rgba(255, 255, 255, .76)";
        public bool ProfileBallEnabled { get; set; }
        public string DinoColorHex { get; set; } = "#FFFFFF";
        public string DinoFoodColorHex { get; set; } = "#45D6C6";
        public string DinoFoodColorSoftRgba { get; set; } = "rgba(69, 214, 198, .24)";
        public string SurfaceHex { get; set; } = "#ffffff";
        public string TextHex { get; set; } = "#0f172a";
        public string MutedHex { get; set; } = "#64748b";
        public string ContrastHex { get; set; } = "#062b2f";
        public decimal FontScale { get; set; } = 1.00m;
    }
}
