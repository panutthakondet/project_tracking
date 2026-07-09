using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;
using System.Globalization;
using System.Net;
using System.Text;

namespace ProjectTracking.Services
{
    public class UserThemeService
    {
        public const string DefaultThemeKey = "projecttracking-default";
        private readonly AppDbContext _context;

        public UserThemeService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<string> GetThemeCssAsync(int? userId, CancellationToken cancellationToken = default)
        {
            var theme = await GetResolvedThemeAsync(userId, cancellationToken);
            return BuildThemeCss(theme);
        }

        public async Task<AppearanceViewModel> GetAppearanceAsync(int userId, CancellationToken cancellationToken = default)
        {
            var presets = await LoadActivePresetsAsync(cancellationToken);
            var defaultPreset = PickDefaultPreset(presets);
            var preference = await _context.UserThemePreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            var selectedPreset = presets.FirstOrDefault(x => x.ThemeId == preference?.ThemeId) ?? defaultPreset;
            var useCustom = preference?.UseCustom ?? false;
            var accent = NormalizeHexOrDefault(preference?.CustomAccentHex, selectedPreset.AccentHex);
            var sidebar = NormalizeHexOrDefault(preference?.CustomSidebarHex, selectedPreset.SidebarHex);
            var bodyBg = NormalizeHexOrDefault(preference?.CustomBodyBgHex, selectedPreset.BodyBgHex);
            var chartPanel = NormalizeHexOrDefault(preference?.CustomChartPanelHex, selectedPreset.ChartPanelHex);
            var fontScale = ClampFontScale(preference?.FontScale ?? 1.00m);
            var resolved = ResolveTheme(selectedPreset, useCustom, accent, sidebar, bodyBg, chartPanel, fontScale);

            return new AppearanceViewModel
            {
                Presets = presets.Select(ToPresetOption).ToList(),
                ThemeId = selectedPreset.ThemeId,
                UseCustom = useCustom,
                CustomAccentHex = accent,
                CustomSidebarHex = sidebar,
                CustomBodyBgHex = bodyBg,
                CustomChartPanelHex = chartPanel,
                FontScale = fontScale,
                EffectiveTheme = resolved
            };
        }

        public async Task<(bool Success, string Message)> SaveAppearanceAsync(
            int userId,
            AppearancePostViewModel model,
            CancellationToken cancellationToken = default)
        {
            var presets = await LoadActivePresetsAsync(cancellationToken);
            var selectedPreset = presets.FirstOrDefault(x => x.ThemeId == model.ThemeId) ?? PickDefaultPreset(presets);

            var accent = NormalizeHexOrDefault(model.CustomAccentHex, selectedPreset.AccentHex);
            var sidebar = NormalizeHexOrDefault(model.CustomSidebarHex, selectedPreset.SidebarHex);
            var bodyBg = NormalizeHexOrDefault(model.CustomBodyBgHex, selectedPreset.BodyBgHex);
            var chartPanel = NormalizeHexOrDefault(model.CustomChartPanelHex, selectedPreset.ChartPanelHex);

            if (model.UseCustom)
            {
                if (!IsHexColor(model.CustomAccentHex) ||
                    !IsHexColor(model.CustomSidebarHex) ||
                    !IsHexColor(model.CustomBodyBgHex) ||
                    !IsHexColor(model.CustomChartPanelHex))
                {
                    return (false, "ค่าสีต้องอยู่ในรูปแบบ #RRGGBB");
                }
            }

            var fontScale = ClampFontScale(model.FontScale);
            var preference = await _context.UserThemePreferences
                .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            if (preference == null)
            {
                preference = new UserThemePreference { UserId = userId };
                _context.UserThemePreferences.Add(preference);
            }

            preference.ThemeId = selectedPreset.ThemeId;
            preference.UseCustom = model.UseCustom;
            preference.CustomAccentHex = model.UseCustom ? accent : null;
            preference.CustomSidebarHex = model.UseCustom ? sidebar : null;
            preference.CustomBodyBgHex = model.UseCustom ? bodyBg : null;
            preference.CustomChartPanelHex = model.UseCustom ? chartPanel : null;
            preference.FontScale = fontScale;
            preference.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync(cancellationToken);
            return (true, "บันทึกธีมเรียบร้อยแล้ว");
        }

        public async Task ResetAppearanceAsync(int userId, CancellationToken cancellationToken = default)
        {
            var preference = await _context.UserThemePreferences
                .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            if (preference == null)
                return;

            _context.UserThemePreferences.Remove(preference);
            await _context.SaveChangesAsync(cancellationToken);
        }

        private async Task<ResolvedThemeViewModel> GetResolvedThemeAsync(int? userId, CancellationToken cancellationToken)
        {
            try
            {
                var presets = await LoadActivePresetsAsync(cancellationToken);
                var defaultPreset = PickDefaultPreset(presets);

                if (userId == null)
                    return ResolveTheme(defaultPreset, false, defaultPreset.AccentHex, defaultPreset.SidebarHex, defaultPreset.BodyBgHex, defaultPreset.ChartPanelHex, 1.00m);

                var preference = await _context.UserThemePreferences
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == userId.Value, cancellationToken);

                if (preference == null)
                    return ResolveTheme(defaultPreset, false, defaultPreset.AccentHex, defaultPreset.SidebarHex, defaultPreset.BodyBgHex, defaultPreset.ChartPanelHex, 1.00m);

                var preset = presets.FirstOrDefault(x => x.ThemeId == preference.ThemeId) ?? defaultPreset;
                var accent = NormalizeHexOrDefault(preference.CustomAccentHex, preset.AccentHex);
                var sidebar = NormalizeHexOrDefault(preference.CustomSidebarHex, preset.SidebarHex);
                var bodyBg = NormalizeHexOrDefault(preference.CustomBodyBgHex, preset.BodyBgHex);
                var chartPanel = NormalizeHexOrDefault(preference.CustomChartPanelHex, preset.ChartPanelHex);
                return ResolveTheme(preset, preference.UseCustom, accent, sidebar, bodyBg, chartPanel, preference.FontScale);
            }
            catch
            {
                var fallback = CreateFallbackPreset();
                return ResolveTheme(fallback, false, fallback.AccentHex, fallback.SidebarHex, fallback.BodyBgHex, fallback.ChartPanelHex, 1.00m);
            }
        }

        private async Task<List<ThemePreset>> LoadActivePresetsAsync(CancellationToken cancellationToken)
        {
            var presets = await _context.ThemePresets
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.ThemeName)
                .ToListAsync(cancellationToken);

            if (presets.Count == 0)
                presets.Add(CreateFallbackPreset());

            return presets;
        }

        private static ThemePreset PickDefaultPreset(IReadOnlyCollection<ThemePreset> presets)
        {
            return presets.FirstOrDefault(x => x.IsDefault) ?? presets.FirstOrDefault() ?? CreateFallbackPreset();
        }

        private static ThemePreset CreateFallbackPreset() => new()
        {
            ThemeId = 0,
            ThemeKey = DefaultThemeKey,
            ThemeName = "ProjectTracking Default",
            IsDefault = true,
            SortOrder = 1,
            AccentHex = "#14b8a6",
            AccentDarkHex = "#0f766e",
            AccentDeepHex = "#0d5f59",
            SidebarHex = "#081c42",
            SidebarDeepHex = "#031934",
            BodyBgHex = "#eef3f9",
            ChartPanelHex = "#081c42",
            SurfaceHex = "#ffffff",
            TextHex = "#0f172a",
            MutedHex = "#64748b",
            ContrastHex = "#062b2f"
        };

        private static ThemePresetOptionViewModel ToPresetOption(ThemePreset preset) => new()
        {
            ThemeId = preset.ThemeId,
            ThemeKey = preset.ThemeKey,
            ThemeName = preset.ThemeName,
            IsDefault = preset.IsDefault,
            AccentHex = NormalizeHexOrDefault(preset.AccentHex, "#14b8a6"),
            AccentDarkHex = NormalizeHexOrDefault(preset.AccentDarkHex, "#0f766e"),
            SidebarHex = NormalizeHexOrDefault(preset.SidebarHex, "#081c42"),
            BodyBgHex = NormalizeHexOrDefault(preset.BodyBgHex, "#eef3f9"),
            ChartPanelHex = NormalizeHexOrDefault(preset.ChartPanelHex, NormalizeHexOrDefault(preset.SidebarHex, "#081c42")),
            TextHex = NormalizeHexOrDefault(preset.TextHex, "#0f172a"),
            ContrastHex = NormalizeHexOrDefault(preset.ContrastHex, GetReadableContrast(NormalizeHexOrDefault(preset.AccentHex, "#14b8a6")))
        };

        private static ResolvedThemeViewModel ResolveTheme(
            ThemePreset preset,
            bool useCustom,
            string customAccentHex,
            string customSidebarHex,
            string customBodyBgHex,
            string customChartPanelHex,
            decimal fontScale)
        {
            var accent = useCustom ? customAccentHex : NormalizeHexOrDefault(preset.AccentHex, "#14b8a6");
            var sidebar = useCustom ? customSidebarHex : NormalizeHexOrDefault(preset.SidebarHex, "#081c42");
            var bodyBg = useCustom ? customBodyBgHex : NormalizeHexOrDefault(preset.BodyBgHex, "#eef3f9");
            var chartPanel = useCustom ? customChartPanelHex : NormalizeHexOrDefault(preset.ChartPanelHex, NormalizeHexOrDefault(preset.SidebarHex, "#081c42"));
            var accentDark = useCustom ? ShiftHex(accent, -0.18) : NormalizeHexOrDefault(preset.AccentDarkHex, ShiftHex(accent, -0.18));
            var accentDeep = useCustom ? ShiftHex(accent, -0.30) : NormalizeHexOrDefault(preset.AccentDeepHex, ShiftHex(accent, -0.30));
            var chartPanelContrast = GetReadableContrast(chartPanel);

            return new ResolvedThemeViewModel
            {
                AccentHex = accent,
                AccentDarkHex = accentDark,
                AccentDeepHex = accentDeep,
                AccentSoftRgba = ToRgba(accent, .18),
                AccentGlowRgba = ToRgba(accent, .30),
                SidebarHex = sidebar,
                SidebarDeepHex = useCustom ? ShiftHex(sidebar, -0.20) : NormalizeHexOrDefault(preset.SidebarDeepHex, ShiftHex(sidebar, -0.20)),
                BodyBgHex = bodyBg,
                ChartPanelHex = chartPanel,
                ChartPanelDeepHex = ShiftHex(chartPanel, -0.18),
                ChartPanelContrastHex = chartPanelContrast,
                ChartPanelContrastMutedRgba = ToRgba(chartPanelContrast, .76),
                SurfaceHex = NormalizeHexOrDefault(preset.SurfaceHex, "#ffffff"),
                TextHex = NormalizeHexOrDefault(preset.TextHex, "#0f172a"),
                MutedHex = NormalizeHexOrDefault(preset.MutedHex, "#64748b"),
                ContrastHex = useCustom ? GetReadableContrast(accent) : NormalizeHexOrDefault(preset.ContrastHex, GetReadableContrast(accent)),
                FontScale = ClampFontScale(fontScale)
            };
        }

        private static string BuildThemeCss(ResolvedThemeViewModel theme)
        {
            var scale = theme.FontScale.ToString("0.00", CultureInfo.InvariantCulture);
            var sidebarContrast = GetReadableContrast(theme.SidebarHex);
            const string chartPrimary = "#2693F4";
            const string chartPrimaryLight = "#4CA5F6";
            const string chartPrimaryDark = "#1269B8";
            const string chartSuccess = "#7BDC49";
            const string chartSuccessLight = "#9EEB74";
            const string chartSuccessDark = "#4EAA2F";
            const string chartWarning = "#FF9F1C";
            const string chartWarningLight = "#FFBC57";
            const string chartWarningDark = "#B86100";
            const string chartDanger = "#F55262";
            const string chartDangerLight = "#FF7A86";
            const string chartDangerDark = "#C92D3D";
            const string chartInfo = "#13D6C9";
            const string chartInfoLight = "#54E3D9";
            const string chartInfoDark = "#087F75";
            const string chartAccentAlt = "#8B5CF6";
            const string chartAccentAltLight = "#A78BFA";
            const string chartAccentAltDark = "#6733B8";
            var sb = new StringBuilder();
            sb.AppendLine(":root {");
            AppendVar(sb, "--pt-accent", theme.AccentHex);
            AppendVar(sb, "--pt-accent-dark", theme.AccentDarkHex);
            AppendVar(sb, "--pt-accent-deep", theme.AccentDeepHex);
            AppendVar(sb, "--pt-accent-soft", theme.AccentSoftRgba);
            AppendVar(sb, "--pt-accent-glow", theme.AccentGlowRgba);
            AppendVar(sb, "--pt-accent-contrast", theme.ContrastHex);
            AppendVar(sb, "--pt-sidebar-bg", theme.SidebarHex);
            AppendVar(sb, "--pt-sidebar-deep", theme.SidebarDeepHex);
            AppendVar(sb, "--pt-sidebar-soft", ToRgba(theme.SidebarHex, .88));
            AppendVar(sb, "--pt-sidebar-panel", ToRgba(theme.SidebarHex, .96));
            AppendVar(sb, "--pt-sidebar-contrast", sidebarContrast);
            AppendVar(sb, "--pt-sidebar-contrast-muted", ToRgba(sidebarContrast, .76));
            AppendVar(sb, "--pt-body-bg", theme.BodyBgHex);
            AppendVar(sb, "--pt-body-bg-soft", ShiftHex(theme.BodyBgHex, -0.04));
            AppendVar(sb, "--pt-chart-panel-bg", theme.ChartPanelHex);
            AppendVar(sb, "--pt-chart-panel-deep", theme.ChartPanelDeepHex);
            AppendVar(sb, "--pt-chart-panel-soft", ToRgba(theme.ChartPanelHex, .88));
            AppendVar(sb, "--pt-chart-panel-field-bg", ToRgba(theme.ChartPanelDeepHex, .72));
            AppendVar(sb, "--pt-chart-panel-row-bg", ShiftHex(theme.ChartPanelHex, -0.08));
            AppendVar(sb, "--pt-chart-panel-row-hover-bg", ShiftHex(theme.ChartPanelHex, 0.04));
            AppendVar(sb, "--pt-chart-panel-glow", ToRgba(theme.AccentHex, .14));
            AppendVar(sb, "--pt-chart-panel-contrast", theme.ChartPanelContrastHex);
            AppendVar(sb, "--pt-chart-panel-contrast-muted", theme.ChartPanelContrastMutedRgba);
            AppendVar(sb, "--pt-surface", theme.SurfaceHex);
            AppendVar(sb, "--pt-surface-soft", ShiftHex(theme.SurfaceHex, -0.04));
            AppendVar(sb, "--pt-text", theme.TextHex);
            AppendVar(sb, "--pt-text-soft", ToRgba(theme.TextHex, .70));
            AppendVar(sb, "--pt-muted", theme.MutedHex);
            AppendVar(sb, "--pt-border", ToRgba(theme.AccentHex, .28));
            AppendVar(sb, "--pt-border-strong", ToRgba(theme.AccentHex, .48));
            AppendVar(sb, "--pt-field-bg", ToRgba(theme.SurfaceHex, .88));
            AppendVar(sb, "--pt-panel-field-bg", ToRgba(theme.SidebarDeepHex, .72));
            AppendVar(sb, "--pt-panel-row-bg", ShiftHex(theme.SidebarHex, -0.08));
            AppendVar(sb, "--pt-panel-row-bg-deep", ShiftHex(theme.SidebarDeepHex, -0.02));
            AppendVar(sb, "--pt-panel-row-hover-bg", ShiftHex(theme.SidebarHex, 0.04));
            AppendVar(sb, "--pt-shadow", ToRgba(theme.SidebarHex, .18));
            AppendVar(sb, "--pt-chart-primary", chartPrimary);
            AppendVar(sb, "--pt-chart-primary-light", chartPrimaryLight);
            AppendVar(sb, "--pt-chart-primary-dark", chartPrimaryDark);
            AppendVar(sb, "--pt-chart-primary-soft", ToRgba(chartPrimary, .18));
            AppendVar(sb, "--pt-chart-success", chartSuccess);
            AppendVar(sb, "--pt-chart-success-light", chartSuccessLight);
            AppendVar(sb, "--pt-chart-success-dark", chartSuccessDark);
            AppendVar(sb, "--pt-chart-success-soft", ToRgba(chartSuccess, .18));
            AppendVar(sb, "--pt-chart-warning", chartWarning);
            AppendVar(sb, "--pt-chart-warning-light", chartWarningLight);
            AppendVar(sb, "--pt-chart-warning-dark", chartWarningDark);
            AppendVar(sb, "--pt-chart-warning-soft", ToRgba(chartWarning, .18));
            AppendVar(sb, "--pt-chart-danger", chartDanger);
            AppendVar(sb, "--pt-chart-danger-light", chartDangerLight);
            AppendVar(sb, "--pt-chart-danger-dark", chartDangerDark);
            AppendVar(sb, "--pt-chart-danger-soft", ToRgba(chartDanger, .18));
            AppendVar(sb, "--pt-chart-info", chartInfo);
            AppendVar(sb, "--pt-chart-info-light", chartInfoLight);
            AppendVar(sb, "--pt-chart-info-dark", chartInfoDark);
            AppendVar(sb, "--pt-chart-info-soft", ToRgba(chartInfo, .18));
            AppendVar(sb, "--pt-chart-alt", chartAccentAlt);
            AppendVar(sb, "--pt-chart-alt-light", chartAccentAltLight);
            AppendVar(sb, "--pt-chart-alt-dark", chartAccentAltDark);
            AppendVar(sb, "--pt-chart-alt-soft", ToRgba(chartAccentAlt, .18));
            AppendVar(sb, "--pt-chart-muted", "#64748B");
            AppendVar(sb, "--pt-menu-accent", theme.AccentHex);
            AppendVar(sb, "--pt-menu-accent-dark", theme.AccentDarkHex);
            AppendVar(sb, "--pt-user-font-scale", scale);
            sb.AppendLine("}");
            sb.AppendLine("html { font-size: calc(14px * var(--pt-user-font-scale)); }");
            sb.AppendLine("@media (min-width: 768px) { html { font-size: calc(16px * var(--pt-user-font-scale)); } }");
            sb.AppendLine("body { background: var(--pt-body-bg) !important; color: var(--pt-text) !important; }");
            sb.AppendLine(".navbar, .v2-sidebar, footer.footer-modern { background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important; }");
            sb.AppendLine("::-webkit-scrollbar-track { background: var(--pt-sidebar-bg) !important; }");
            sb.AppendLine("::-webkit-scrollbar-thumb { background: var(--pt-accent-dark) !important; border-color: var(--pt-sidebar-bg) !important; }");
            const string dropdownScrollbarSelector =
                ":is(select, .form-select, .dropdown-menu, .dropdown-menu .inner, .pt-search-select__dropdown, " +
                ".select2-results, .select2-results__options, .select2-dropdown, .choices__list, " +
                ".choices__list--dropdown, .choices__list--dropdown .choices__list, .ts-dropdown, .ts-dropdown-content)";
            sb.AppendLine($"{dropdownScrollbarSelector} {{ scrollbar-width: thin !important; scrollbar-color: var(--pt-accent-dark) transparent !important; }}");
            sb.AppendLine($"{dropdownScrollbarSelector}::-webkit-scrollbar {{ width: 6px !important; height: 6px !important; }}");
            sb.AppendLine($"{dropdownScrollbarSelector}::-webkit-scrollbar-track {{ background: transparent !important; border-radius: 999px !important; }}");
            sb.AppendLine($"{dropdownScrollbarSelector}::-webkit-scrollbar-thumb {{ background: var(--pt-accent-dark) !important; border: 1px solid transparent !important; background-clip: content-box !important; border-radius: 999px !important; }}");
            sb.AppendLine($"{dropdownScrollbarSelector}::-webkit-scrollbar-thumb:hover {{ background: var(--pt-accent) !important; background-clip: content-box !important; }}");
            sb.AppendLine(".navbar.navbar-dark .navbar-nav .nav-link.active-menu, .btn-primary, .system-update-ack { background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important; color: var(--pt-accent-contrast) !important; }");
            sb.AppendLine(".navbar.navbar-dark .navbar-nav .nav-link:hover, .navbar.navbar-dark .navbar-nav .nav-link:focus, .navbar.navbar-dark .navbar-nav .show > .nav-link { background: var(--pt-accent-soft) !important; }");
            sb.AppendLine(".btn-info, .bg-info, .form-check-input:checked, .active > .page-link, .page-link.active { background-color: var(--pt-accent) !important; border-color: var(--pt-accent) !important; color: var(--pt-accent-contrast) !important; }");
            sb.AppendLine(".btn-outline-info, .btn-outline-primary, .page-link, .footer-modern .footer-brand, .btn-logout { color: var(--pt-accent-dark) !important; border-color: var(--pt-accent) !important; }");
            sb.AppendLine(".form-control:focus, .form-select:focus, .btn:focus, .btn:active:focus, .form-check-input:focus { border-color: var(--pt-accent) !important; box-shadow: 0 0 0 4px var(--pt-accent-soft) !important; }");
            sb.AppendLine(".pt-swal-confirm { background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important; color: var(--pt-accent-contrast) !important; }");
            sb.AppendLine(@"
.v2-shell,
.v2-page {
    background: var(--pt-body-bg) !important;
}

.v2-sidebar {
    border-right-color: var(--pt-accent) !important;
    box-shadow: 18px 0 38px var(--pt-shadow), inset -1px 0 0 var(--pt-border) !important;
}

.v2-sidebar::before {
    background:
        radial-gradient(circle at 50% 7%, var(--pt-accent-soft), transparent 30%),
        linear-gradient(90deg, rgba(255,255,255,.05), transparent 20%, transparent 80%, rgba(0,0,0,.18)) !important;
}

.v2-avatar {
    border-color: var(--pt-accent) !important;
    box-shadow: 0 0 0 7px var(--pt-accent-soft), 0 18px 30px var(--pt-shadow) !important;
}

.v2-sidebar .navbar-dark .navbar-nav .nav-link,
.v2-sidebar .navbar-dark .navbar-nav .dropdown-toggle,
.v2-sidebar .dropdown-menu {
    color: var(--pt-chart-panel-contrast) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-soft), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border) !important;
}

.v2-sidebar .dropdown-item {
    color: var(--pt-chart-panel-contrast-muted) !important;
}

.v2-sidebar .dropdown-item:hover,
.v2-sidebar .dropdown-item:focus {
    color: var(--pt-chart-panel-contrast) !important;
    background: var(--pt-chart-panel-field-bg) !important;
}

.v2-avatar-upload {
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    color: var(--pt-accent-contrast) !important;
    box-shadow: 0 8px 16px var(--pt-accent-soft), inset 0 1px 0 rgba(255,255,255,.20) !important;
}

.navbar.navbar-dark .navbar-nav .nav-link.active-menu {
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 12px 24px var(--pt-accent-soft), inset 0 1px 0 rgba(255,255,255,.14) !important;
}

.navbar.navbar-dark .navbar-nav .nav-link:hover,
.navbar.navbar-dark .navbar-nav .nav-link:focus,
.navbar.navbar-dark .navbar-nav .show > .nav-link {
    color: var(--pt-chart-panel-contrast) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-row-hover-bg), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

.navbar.navbar-dark .navbar-nav .nav-link.active-menu:hover,
.navbar.navbar-dark .navbar-nav .nav-link.active-menu:focus,
.navbar.navbar-dark .navbar-nav .show > .nav-link.active-menu {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    border-color: var(--pt-border-strong) !important;
}

.dropdown-header,
.footer-modern strong,
.dashboard-footer strong {
    color: var(--pt-accent) !important;
}

.dropdown-divider {
    border-color: var(--pt-border) !important;
}

main > :is(h1, h2, h3):first-child,
main > .container:first-child > :is(h1, h2, h3):first-child,
main > .container-fluid:first-child > :is(h1, h2, h3):first-child,
.index-hero {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 84% 12%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 18px 40px var(--pt-shadow), inset 0 1px 0 rgba(255,255,255,.10) !important;
}

.index-title,
.index-subtitle,
.index-eyebrow {
    color: var(--pt-sidebar-contrast) !important;
}

.index-subtitle,
.index-eyebrow {
    opacity: .86;
}

.index-actions :is(.btn, .btn-primary, .btn-info, .btn-success, .btn-warning, .btn-danger, .btn-secondary, .btn-light, .btn-outline-primary, .btn-outline-info, .btn-outline-success, .btn-outline-warning, .btn-outline-danger, .btn-outline-secondary, .btn-outline-light) {
    color: var(--pt-sidebar-contrast) !important;
    background: var(--pt-accent-soft) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.12), 0 10px 20px var(--pt-shadow) !important;
}

.index-actions :is(.btn-primary, .btn-info, .btn-success, .btn-light),
.index-actions :is(.btn, .btn-primary, .btn-info, .btn-success, .btn-warning, .btn-danger, .btn-secondary, .btn-light, .btn-outline-primary, .btn-outline-info, .btn-outline-success, .btn-outline-warning, .btn-outline-danger, .btn-outline-secondary, .btn-outline-light):hover {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important;
    border-color: transparent !important;
}

main :is(.card, .table-responsive):not(.glass-panel):not(.kpi-card):not(.project-overview-table) {
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
    box-shadow: 0 12px 28px var(--pt-shadow) !important;
}

main :is(.form-control, .form-select, .pt-search-select__input) {
    background-color: var(--pt-field-bg) !important;
    border-color: var(--pt-border) !important;
    color: var(--pt-text) !important;
}

main :is(.form-control, .form-select, .pt-search-select__input)::placeholder {
    color: var(--pt-muted) !important;
}

.pt-search-select__dropdown {
    border-color: var(--pt-border) !important;
    box-shadow: 0 18px 34px var(--pt-shadow) !important;
}

.pt-search-select__option:hover,
.pt-search-select__option:focus,
.pt-search-select__option.is-selected {
    background: var(--pt-accent-soft) !important;
    color: var(--pt-text) !important;
}

.dashboard-view {
    --panel: var(--pt-sidebar-bg);
    color: var(--pt-text) !important;
    background:
        radial-gradient(circle at 18% 16%, var(--pt-accent-soft), transparent 28%),
        linear-gradient(180deg, var(--pt-body-bg) 0%, var(--pt-body-bg-soft) 100%) !important;
}

.dashboard-view .kpi-card {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 16% 28%, color-mix(in srgb, var(--kpi-accent) 26%, transparent), transparent 18%),
        radial-gradient(circle at 80% 95%, color-mix(in srgb, var(--kpi-accent) 18%, transparent), transparent 28%),
        linear-gradient(135deg, var(--pt-sidebar-bg) 0%, var(--pt-sidebar-deep) 100%) !important;
}

.dashboard-view .kpi-card :is(p, strong, .kpi-menu) {
    color: var(--pt-sidebar-contrast) !important;
}

.dashboard-view .kpi-card small {
    color: var(--pt-sidebar-contrast-muted) !important;
}

.dashboard-global-search input {
    color: var(--pt-text) !important;
    -webkit-text-fill-color: var(--pt-text) !important;
    background: linear-gradient(180deg, var(--pt-field-bg), var(--pt-surface-soft)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 12px 24px var(--pt-shadow), inset 0 1px 0 rgba(255,255,255,.68) !important;
}

.dashboard-global-search input:focus {
    border-color: var(--pt-accent) !important;
    box-shadow: 0 14px 30px var(--pt-accent-soft), 0 0 0 3px var(--pt-accent-soft) !important;
}

.dashboard-global-search-icon,
.dashboard-view .top-icon-button {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 12px 24px var(--pt-accent-soft), inset 0 1px 0 rgba(255,255,255,.18) !important;
}

.dashboard-global-search-clear,
.dashboard-online-users,
.dashboard-view :is(.dashboard-global-tool, .dashboard-global-tool.notification, .dashboard-global-tool.open-work, .dashboard-global-tool.attendance) {
    color: var(--pt-accent-dark) !important;
    background: var(--pt-field-bg) !important;
    border-color: var(--pt-border) !important;
    box-shadow: 0 10px 20px var(--pt-shadow), inset 0 1px 0 rgba(255,255,255,.50) !important;
}

.dashboard-view :is(.glass-panel, .panel-project-overview) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 82% 16%, var(--pt-accent-soft), transparent 32%),
        linear-gradient(180deg, var(--pt-sidebar-panel), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.10), 0 16px 32px var(--pt-shadow) !important;
}

.dashboard-view :is(.project-overview-title-row h2, .issues-overview-head h2, .dashboard-card-title h2) {
    color: var(--pt-sidebar-contrast) !important;
}

.dashboard-view :is(.project-overview-title-row small, .issues-overview-head small, .dashboard-card-title em, .dashboard-card-title h2 small) {
    color: var(--pt-sidebar-contrast-muted) !important;
}

.dashboard-view :is(.overview-mini, .yearly-chart, .project-status-donut-layout, .owner-overview-chart, .owner-overview-member, .owner-overview-footer, .metric-card, .time-detail-grid > span, .dashboard-section-foot) {
    color: var(--pt-chart-panel-contrast) !important;
    background:
        radial-gradient(circle at 50% 40%, var(--pt-chart-panel-glow), transparent 42%),
        linear-gradient(180deg, var(--pt-chart-panel-soft), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

.dashboard-view .owner-overview-chart {
    background:
        linear-gradient(90deg, var(--pt-chart-panel-contrast-muted) 1px, transparent 1px) 0 0 / 1px var(--owner-bar-area) no-repeat,
        linear-gradient(var(--pt-chart-panel-contrast-muted) 1px, transparent 1px) 0 var(--owner-bar-area) / 100% 1px no-repeat,
        repeating-linear-gradient(to bottom, rgba(255,255,255,.16) 0 1px, transparent 1px calc(var(--owner-bar-area) / 5)) 0 0 / 100% var(--owner-bar-area) no-repeat,
        linear-gradient(180deg, var(--pt-chart-panel-soft), var(--pt-chart-panel-deep)) !important;
}

.dashboard-view :is(.overview-mini-head h3, .project-status-card-title, .owner-overview-member b, .metric-list b, .metric-name, .meeting-row b, .activity-row b) {
    color: var(--pt-chart-panel-contrast) !important;
}

.dashboard-view :is(.overview-mini-head small, .project-status-updated, .owner-overview-y-axis, .owner-overview-y-title, .owner-overview-member small, .owner-overview-footer, .dashboard-section-foot, .meeting-row small, .activity-row small, .activity-row em, .workload-row .workload-name small, .time-detail-grid small) {
    color: var(--pt-chart-panel-contrast-muted) !important;
}

.dashboard-view :is(.project-overview-action, .project-overview-action.team, .overview-detail-btn, .overview-detail-btn.green, .overview-detail-btn.orange, .panel-filter) {
    color: var(--pt-sidebar-contrast) !important;
    background: var(--pt-accent-soft) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.10), 0 12px 22px var(--pt-shadow) !important;
}

.dashboard-view :is(.project-overview-action, .project-overview-action.team, .overview-detail-btn, .overview-detail-btn.green, .overview-detail-btn.orange, .panel-filter):hover {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important;
}

.dashboard-view .project-overview-search input {
    color: var(--pt-sidebar-contrast) !important;
    -webkit-text-fill-color: var(--pt-sidebar-contrast) !important;
    caret-color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(180deg, var(--pt-panel-field-bg), var(--pt-sidebar-soft)) !important;
    border-color: var(--pt-border) !important;
}

.dashboard-view .project-overview-search input::placeholder,
.dashboard-view .project-overview-search i {
    color: var(--pt-sidebar-contrast-muted) !important;
    -webkit-text-fill-color: var(--pt-sidebar-contrast-muted) !important;
}

.dashboard-view .project-overview-search input:focus {
    border-color: var(--pt-accent) !important;
    box-shadow: 0 0 0 3px var(--pt-accent-soft) !important;
}

.dashboard-view .project-overview-scroll {
    scrollbar-color: var(--pt-accent) var(--pt-sidebar-soft) !important;
}

.dashboard-view .project-overview-scroll::-webkit-scrollbar-track {
    background: var(--pt-sidebar-soft) !important;
}

.dashboard-view .project-overview-scroll::-webkit-scrollbar-thumb {
    background: linear-gradient(180deg, var(--pt-accent), var(--pt-accent-dark)) !important;
}

.dashboard-view :is(.progress-list, .workload-list, .yearly-plot) {
    scrollbar-color: var(--pt-accent) var(--pt-sidebar-soft) !important;
}

.dashboard-view :is(.progress-list, .workload-list, .yearly-plot)::-webkit-scrollbar-track {
    background: var(--pt-sidebar-soft) !important;
}

.dashboard-view :is(.progress-list, .workload-list, .yearly-plot)::-webkit-scrollbar-thumb {
    background: linear-gradient(90deg, var(--pt-accent), var(--pt-accent-dark)) !important;
}

.dashboard-view .project-overview-row {
    color: var(--pt-chart-panel-contrast) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-row-bg), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.11), 0 14px 26px var(--pt-shadow) !important;
}

.dashboard-view .project-overview-row:hover {
    border-color: var(--pt-border-strong) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-row-hover-bg), var(--pt-chart-panel-row-bg)) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.14), 0 18px 32px var(--pt-shadow) !important;
}

.dashboard-view .project-overview-head {
    border-bottom-color: var(--pt-border) !important;
    color: var(--pt-sidebar-contrast-muted) !important;
}

.dashboard-view :is(.project-overview-name, .project-overview-row time, .project-overview-date) {
    color: var(--pt-chart-panel-contrast) !important;
}

.dashboard-view .project-overview-mobile-label,
.dashboard-view .project-overview-date::before {
    color: var(--pt-chart-panel-contrast-muted) !important;
}

.dashboard-view :is(.dot.green, .dot.success) { color: var(--pt-chart-success) !important; background: currentColor !important; }
.dashboard-view :is(.dot.blue, .dot.primary, .dot.info) { color: var(--pt-chart-primary) !important; background: currentColor !important; }
.dashboard-view :is(.dot.orange, .dot.warning) { color: var(--pt-chart-warning) !important; background: currentColor !important; }
.dashboard-view :is(.dot.pink, .dot.danger) { color: var(--pt-chart-danger) !important; background: currentColor !important; }
.dashboard-view :is(.dot.purple, .dot.violet) { color: var(--pt-chart-alt) !important; background: currentColor !important; }
.dashboard-view :is(.dot.cyan, .dot.lime) { color: var(--pt-chart-info) !important; background: currentColor !important; }
.dashboard-view :is(.dot.dark, .dot.secondary, .dot.muted) { color: var(--pt-chart-muted) !important; background: currentColor !important; }

.dashboard-view .line.green { color: var(--pt-chart-success) !important; stroke: var(--pt-chart-success) !important; }
.dashboard-view .line.blue { color: var(--pt-chart-primary) !important; stroke: var(--pt-chart-primary) !important; }
.dashboard-view .line.orange { color: var(--pt-chart-warning) !important; stroke: var(--pt-chart-warning) !important; }
.dashboard-view .line.pink { color: var(--pt-chart-danger) !important; stroke: var(--pt-chart-danger) !important; }

.dashboard-view .project-overview-status {
    color: var(--pt-chart-primary) !important;
    background: var(--pt-chart-primary-soft) !important;
    border-color: var(--pt-chart-primary) !important;
}

.dashboard-view .project-overview-status.green {
    color: var(--pt-chart-success) !important;
    background: var(--pt-chart-success-soft) !important;
    border-color: var(--pt-chart-success) !important;
}

.dashboard-view .project-overview-status.orange {
    color: var(--pt-chart-warning) !important;
    background: var(--pt-chart-warning-soft) !important;
    border-color: var(--pt-chart-warning) !important;
}

.dashboard-view .project-overview-status.pink {
    color: var(--pt-chart-danger) !important;
    background: var(--pt-chart-danger-soft) !important;
    border-color: var(--pt-chart-danger) !important;
}

.dashboard-view .project-overview-status.purple {
    color: var(--pt-chart-alt) !important;
    background: var(--pt-chart-alt-soft) !important;
    border-color: var(--pt-chart-alt) !important;
}

.dashboard-view .project-overview-status.blue,
.dashboard-view .project-overview-status.cyan,
.dashboard-view .project-overview-status.lime {
    color: var(--pt-chart-primary) !important;
    background: var(--pt-chart-primary-soft) !important;
    border-color: var(--pt-chart-primary) !important;
}

.dashboard-view .project-status-donut {
    box-shadow: 0 0 30px var(--pt-chart-primary-soft), 0 14px 30px var(--pt-shadow) !important;
}

.dashboard-view :is(.project-status-donut-layout, .overview-mini, .panel-issues) .metric-list > span {
    color: var(--pt-chart-panel-contrast) !important;
    border-color: var(--pt-border) !important;
    background: var(--pt-chart-panel-field-bg) !important;
}

.dashboard-view :is(.workload-row, .activity-row, .meeting-row, .time-number, .time-detail-grid > span) {
    color: var(--pt-chart-panel-contrast) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-row-bg), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border) !important;
}

.dashboard-view :is(.workload-row:hover, .activity-row:hover, .meeting-row:hover) {
    color: var(--pt-chart-panel-contrast) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-row-hover-bg), var(--pt-chart-panel-row-bg)) !important;
}

.dashboard-view .bar-group span:nth-child(1) {
    color: var(--pt-chart-success) !important;
    background: linear-gradient(180deg, var(--pt-chart-success-light) 0%, var(--pt-chart-success) 54%, var(--pt-chart-success-dark) 100%) !important;
}

.dashboard-view .bar-group span:nth-child(2) {
    color: var(--pt-chart-primary) !important;
    background: linear-gradient(180deg, var(--pt-chart-primary-light) 0%, var(--pt-chart-primary) 55%, var(--pt-chart-primary-dark) 100%) !important;
}

.dashboard-view .owner-bar.done i,
.dashboard-view .progress-track .green,
.dashboard-view .time-pill.green {
    background: linear-gradient(180deg, var(--pt-chart-success-light), var(--pt-chart-success-dark)) !important;
}

.dashboard-view .owner-bar.working i,
.dashboard-view .progress-track .orange,
.dashboard-view .time-pill.orange {
    background: linear-gradient(180deg, var(--pt-chart-warning-light), var(--pt-chart-warning-dark)) !important;
}

.dashboard-view .owner-bar.stuck i,
.dashboard-view .progress-track .pink,
.dashboard-view .time-pill.pink {
    background: linear-gradient(180deg, var(--pt-chart-danger-light), var(--pt-chart-danger-dark)) !important;
}

.dashboard-view .owner-bar.issue-open i,
.dashboard-view .progress-track .purple,
.dashboard-view .time-pill.purple {
    background: linear-gradient(180deg, var(--pt-chart-alt-light), var(--pt-chart-alt-dark)) !important;
}

.dashboard-view .owner-bar.support-open i,
.dashboard-view .progress-track .blue,
.dashboard-view .progress-track .cyan,
.dashboard-view .progress-track .lime,
.dashboard-view .time-pill.blue {
    background: linear-gradient(180deg, var(--pt-chart-primary-light), var(--pt-chart-primary-dark)) !important;
}

:root {
    --meeting-navy: var(--pt-sidebar-bg);
    --meeting-teal: var(--pt-menu-accent);
    --meeting-teal-dark: var(--pt-menu-accent-dark);
    --meeting-blue: var(--pt-chart-primary);
    --meeting-purple: var(--pt-chart-alt);
    --meeting-orange: var(--pt-chart-warning);
    --meeting-pink: var(--pt-chart-danger);
    --meeting-border: var(--pt-border);
}

main.route-controller-meetings .calendar-card,
main.route-controller-meetings .meeting-list-card,
main.route-controller-meetings .meeting-card,
main.route-controller-meetings .meeting-detail-card {
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
    box-shadow: 0 16px 34px var(--pt-shadow) !important;
}

main.route-controller-meetings .calendar-card__toolbar,
main.route-controller-meetings :is(.meeting-hero, .meeting-form-hero, .meeting-detail-hero) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 10%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

main.route-controller-meetings .calendar-heading__date {
    color: var(--pt-text) !important;
    background: var(--pt-field-bg) !important;
    border: 1px solid var(--pt-border) !important;
    box-shadow: 0 12px 26px var(--pt-shadow) !important;
}

main.route-controller-meetings :is(.calendar-heading__eyebrow, .calendar-heading__title, .calendar-heading__subtitle) {
    color: var(--pt-sidebar-contrast) !important;
}

main.route-controller-meetings .calendar-heading__subtitle,
main.route-controller-meetings .calendar-heading__eyebrow {
    opacity: .82;
}

main.route-controller-meetings :is(.calendar-heading__action, .fc .fc-button-primary, .btn-primary) {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    border-color: transparent !important;
    box-shadow: 0 10px 20px var(--pt-accent-soft) !important;
}

main.route-controller-meetings :is(.calendar-heading__action:not(.primary), .btn-outline-light, .btn-outline-secondary, .btn-outline-primary) {
    color: var(--pt-sidebar-contrast) !important;
    background: var(--pt-accent-soft) !important;
    border-color: var(--pt-border-strong) !important;
}

main.route-controller-meetings .fc .fc-button-primary:not(:disabled).fc-button-active,
main.route-controller-meetings .fc .fc-button-primary:not(:disabled):active,
main.route-controller-meetings .fc .fc-button-primary:not(:disabled):hover,
main.route-controller-meetings .fc .fc-button-primary:not(:disabled):focus {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-chart-primary-light), var(--pt-chart-primary-dark)) !important;
    border-color: transparent !important;
}

main.route-controller-meetings .calendar-card__body {
    background: var(--pt-body-bg-soft) !important;
}

main.route-controller-meetings .fc {
    color: var(--pt-text) !important;
}

main.route-controller-meetings .fc .fc-toolbar-title,
main.route-controller-meetings .fc .fc-daygrid-day-number,
main.route-controller-meetings :is(.meeting-project-text, .meeting-desc, .meeting-detail-value, .meeting-project-name) {
    color: var(--pt-text) !important;
}

main.route-controller-meetings .fc .fc-scrollgrid,
main.route-controller-meetings .fc .fc-list {
    border-color: var(--pt-border) !important;
    background: var(--pt-surface) !important;
    box-shadow: 0 12px 28px var(--pt-shadow) !important;
}

main.route-controller-meetings .fc .fc-col-header-cell,
main.route-controller-meetings .calendar-card .fc .fc-col-header-cell,
main.route-controller-meetings .fc .fc-list-day-cushion,
main.route-controller-meetings .meeting-list-card .card-header {
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border) !important;
}

main.route-controller-meetings .fc .fc-col-header-cell .fc-col-header-cell-cushion,
main.route-controller-meetings .calendar-card .fc .fc-col-header-cell .fc-col-header-cell-cushion {
    color: var(--pt-sidebar-contrast) !important;
}

main.route-controller-meetings .fc .fc-daygrid-day,
main.route-controller-meetings .fc .fc-daygrid-day.fc-day-other,
main.route-controller-meetings .fc .fc-day-today,
main.route-controller-meetings .fc .fc-timegrid-axis,
main.route-controller-meetings .fc .fc-timegrid-slot-label {
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
}

main.route-controller-meetings .fc .fc-daygrid-day.fc-day-other {
    background: var(--pt-surface-soft) !important;
}

main.route-controller-meetings .fc .fc-day-today .fc-daygrid-day-number {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    box-shadow: 0 8px 18px var(--pt-accent-soft) !important;
}

main.route-controller-meetings :is(.fc-event, .meeting-row) {
    --meeting-event-bg-start: var(--pt-field-bg);
    --meeting-event-bg-end: var(--pt-surface-soft);
    --meeting-event-border: var(--pt-border);
    color: var(--pt-text) !important;
    background: linear-gradient(180deg, var(--meeting-event-bg-start), var(--meeting-event-bg-end)) !important;
    border-color: var(--meeting-event-border) !important;
    box-shadow: 0 8px 18px var(--pt-shadow) !important;
}

main.route-controller-meetings :is(.fc-event-title, .meeting-event-title, .meeting-event-subtitle, .meeting-event-meta) {
    color: var(--pt-text) !important;
}

main.route-controller-meetings :is(.fc-event.customer-event, .meeting-row.customer-event) {
    --meeting-event-bg-end: var(--pt-chart-success-soft);
    --meeting-event-border: var(--pt-chart-success);
}

main.route-controller-meetings :is(.fc-event.team-event, .meeting-row.team-event, .fc-event.other-event, .meeting-row.other-event) {
    --meeting-event-bg-end: var(--pt-chart-primary-soft);
    --meeting-event-border: var(--pt-chart-primary);
}

main.route-controller-meetings :is(.fc-event.executive-event, .meeting-row.executive-event) {
    --meeting-event-bg-end: var(--pt-chart-danger-soft);
    --meeting-event-border: var(--pt-chart-danger);
}

main.route-controller-meetings :is(.fc-event.manager-event, .meeting-row.manager-event) {
    --meeting-event-bg-end: var(--pt-chart-alt-soft);
    --meeting-event-border: var(--pt-chart-alt);
}

main.route-controller-meetings :is(.fc-event.vendor-event, .meeting-row.vendor-event) {
    --meeting-event-bg-end: var(--pt-chart-warning-soft);
    --meeting-event-border: var(--pt-chart-warning);
}

main.route-controller-meetings :is(.meeting-icon, .meeting-project-icon) {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
}

main.route-controller-meetings .fc-tooltip {
    color: var(--pt-sidebar-contrast) !important;
    background: var(--pt-sidebar-bg) !important;
    border-color: var(--pt-border) !important;
    box-shadow: 0 12px 28px var(--pt-shadow) !important;
}

main.route-controller-projects .project-filter-form :is(.form-control, .form-select, .pt-search-select__input),
main.route-controller-projects .project-table-wrapper,
main.route-controller-projects .project-card {
    border-color: var(--pt-border) !important;
}

main.route-controller-projects .project-card {
    background: var(--pt-surface) !important;
    box-shadow: 0 12px 28px var(--pt-shadow) !important;
}

main.route-controller-projects .project-row-number {
    color: var(--pt-accent-dark) !important;
    background: var(--pt-accent-soft) !important;
    border-color: var(--pt-border-strong) !important;
}

main.route-controller-projects .project-table thead,
main.route-controller-projects .project-table thead .sticky-col-1,
main.route-controller-projects .project-table thead .sticky-col-2 {
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
}

main.route-controller-projects :is(.sticky-col-1, .sticky-col-2) {
    background: var(--pt-surface) !important;
}

main.route-controller-projectissues :is(.index-hero, .issue-hero, .dev-issue-hero) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 10%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 18px 40px var(--pt-shadow), inset 0 1px 0 rgba(255,255,255,.10) !important;
}

main.route-controller-projectissues :is(.index-hero, .issue-hero, .dev-issue-hero) :is(.index-title, .index-eyebrow, .issue-title, .issue-eyebrow, .dev-title, .dev-eyebrow, .dev-issue-title) {
    color: var(--pt-sidebar-contrast) !important;
}

main.route-controller-projectissues :is(.index-hero, .issue-hero, .dev-issue-hero) :is(.index-subtitle, .issue-eyebrow, .dev-eyebrow, .dev-subtitle, .dev-issue-subtitle) {
    color: var(--pt-sidebar-contrast-muted) !important;
}

main.route-controller-projectissues :is(.issue-hero-actions, .dev-hero-actions, .index-actions) :is(.btn, .btn-primary, .btn-outline-light, .btn-outline-secondary, .btn-outline-primary) {
    color: var(--pt-sidebar-contrast) !important;
    background: var(--pt-accent-soft) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.12), 0 10px 20px var(--pt-shadow) !important;
}

main.route-controller-projectissues :is(.issue-hero-actions, .dev-hero-actions, .index-actions) :is(.btn-primary, .btn:hover, .btn:focus) {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important;
    border-color: transparent !important;
}

main.route-controller-projectissues :is(.issue-git-table, .dev-git-table) thead,
main.route-controller-projectissues :is(.issue-git-table, .dev-git-table) thead th {
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border) !important;
}

main.route-controller-reports .report-center-hero,
main.route-controller-weeklyreports :is(.weekly-hero, .report-hero),
main.route-controller-assignedemployeesreport .index-hero,
main.route-controller-statusapprovals .index-hero,
main.route-controller-projectstatus .project-status-header.index-hero {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 10%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 18px 40px var(--pt-shadow), inset 0 1px 0 rgba(255,255,255,.10) !important;
}

main.route-controller-reports .report-center-hero :is(.index-eyebrow, .index-title, .index-subtitle, .report-hero-stats strong),
main.route-controller-weeklyreports :is(.weekly-hero, .report-hero) :is(.weekly-eyebrow, .weekly-title, .report-eyebrow, h1),
main.route-controller-assignedemployeesreport .index-hero :is(.index-eyebrow, .index-title, .index-subtitle),
main.route-controller-statusapprovals .index-hero :is(.index-eyebrow, .index-title, .index-subtitle),
main.route-controller-projectstatus .project-status-header.index-hero :is(.index-eyebrow, .index-title, .index-subtitle) {
    color: var(--pt-sidebar-contrast) !important;
}

main.route-controller-reports .report-center-hero .report-hero-stats span,
main.route-controller-weeklyreports :is(.weekly-hero, .report-hero) :is(.weekly-subtitle, .report-meta) {
    color: var(--pt-sidebar-contrast-muted) !important;
}

main.route-controller-weeklyreports :is(.weekly-hero, .report-hero) :is(.weekly-btn, .report-btn),
main.route-controller-projectstatus .project-status-header.index-hero .status-back-link {
    color: var(--pt-sidebar-contrast) !important;
    background: var(--pt-accent-soft) !important;
    border-color: var(--pt-border-strong) !important;
}

main.route-controller-weeklyreports :is(.weekly-hero, .report-hero) :is(.weekly-btn.primary, .report-btn.primary, .weekly-btn:hover, .report-btn:hover),
main.route-controller-projectstatus .project-status-header.index-hero .status-back-link:hover,
main.route-controller-projectstatus .project-status-header.index-hero .status-back-link:focus {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important;
    border-color: transparent !important;
    box-shadow: 0 10px 20px var(--pt-accent-soft) !important;
}

main:is(.route-controller-reports, .route-controller-weeklyreports, .route-controller-assignedemployeesreport, .route-controller-statusapprovals, .route-controller-projectstatus) table thead,
main:is(.route-controller-reports, .route-controller-weeklyreports, .route-controller-assignedemployeesreport, .route-controller-statusapprovals, .route-controller-projectstatus) table thead th {
    --bs-table-bg: var(--pt-sidebar-bg) !important;
    --bs-table-color: var(--pt-sidebar-contrast) !important;
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border) !important;
}

body.pt-standalone-report {
    color: #10233f !important;
    background: #fff !important;
}

body.pt-standalone-report .print-header {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 10%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

body.pt-standalone-report .print-header :is(h1, h2, h3, div, p, span, strong, b) {
    color: var(--pt-sidebar-contrast) !important;
}

body.pt-standalone-report table thead,
body.pt-standalone-report table thead th {
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border) !important;
}

@media print {
    body.pt-standalone-report {
        color: #10233f !important;
        background: #fff !important;
    }
}

main.route-controller-phaseworkload {
    color: var(--pt-text) !important;
    background:
        radial-gradient(circle at 18% 12%, var(--pt-accent-soft), transparent 26%),
        linear-gradient(180deg, var(--pt-body-bg), var(--pt-body-bg-soft)) !important;
}

main.route-controller-phaseworkload .workload-calendar-card {
    color: var(--pt-text) !important;
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
    box-shadow: 0 16px 38px var(--pt-shadow) !important;
}

main.route-controller-phaseworkload .workload-content-card {
    color: var(--pt-chart-panel-contrast) !important;
    background:
        radial-gradient(circle at 84% 12%, var(--pt-chart-panel-glow), transparent 30%),
        linear-gradient(180deg, var(--pt-chart-panel-soft), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 16px 38px var(--pt-shadow) !important;
}

main.route-controller-phaseworkload .workload-calendar-toolbar {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 86% 12%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

main.route-controller-phaseworkload :is(.workload-calendar-eyebrow, .workload-calendar-title, .workload-calendar-subtitle) {
    color: var(--pt-sidebar-contrast) !important;
}

main.route-controller-phaseworkload :is(.workload-calendar-eyebrow, .workload-calendar-subtitle) {
    opacity: .82;
}

main.route-controller-phaseworkload .workload-calendar-date {
    color: var(--pt-text) !important;
    background: var(--pt-field-bg) !important;
    border: 1px solid var(--pt-border) !important;
    box-shadow: 0 12px 26px var(--pt-shadow) !important;
}

main.route-controller-phaseworkload .workload-calendar-action {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    border-color: transparent !important;
    box-shadow: 0 10px 20px var(--pt-accent-soft) !important;
}

main.route-controller-phaseworkload :is(.filter-toolbar, .workload-table-topbar) {
    color: var(--pt-chart-panel-contrast) !important;
}

main.route-controller-phaseworkload .workload-filter-field {
    color: var(--pt-chart-panel-contrast) !important;
}

main.route-controller-phaseworkload .workload-filter-field > span {
    color: var(--pt-chart-panel-contrast-muted) !important;
}

main.route-controller-phaseworkload :is(.workload-filter-select, .emp-select-fixed, #empFilter, .month-select, .form-select) {
    color: var(--pt-chart-panel-contrast) !important;
    background-color: var(--pt-chart-panel-field-bg) !important;
    border-color: var(--pt-border) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.12) !important;
}

main.route-controller-phaseworkload .workload-responsive-wrap {
    background: linear-gradient(180deg, var(--pt-chart-panel-row-bg), var(--pt-chart-panel-deep)) !important;
    border: 1px solid var(--pt-border-strong) !important;
    box-shadow: 0 12px 28px var(--pt-shadow) !important;
}

main.route-controller-phaseworkload .workload-view-tabs {
    background: var(--pt-chart-panel-field-bg) !important;
    border-color: var(--pt-border) !important;
    box-shadow: 0 8px 20px var(--pt-shadow) !important;
}

main.route-controller-phaseworkload .workload-view-tab {
    color: var(--pt-chart-panel-contrast) !important;
}

main.route-controller-phaseworkload .workload-view-tab:hover {
    color: var(--pt-accent-dark) !important;
    background: var(--pt-accent-soft) !important;
}

main.route-controller-phaseworkload .workload-view-tab.is-active {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    box-shadow: 0 8px 16px var(--pt-accent-soft) !important;
}

main.route-controller-phaseworkload .workload-status-row-title {
    color: var(--pt-chart-panel-contrast-muted) !important;
}

main.route-controller-phaseworkload .workload-status-divider {
    background: var(--pt-border) !important;
}

main.route-controller-phaseworkload .workload-loading-box {
    color: var(--pt-chart-panel-contrast) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-row-bg), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 22px 50px var(--pt-shadow) !important;
}

main.route-controller-phaseworkload .workload-loading-spinner {
    border-color: var(--pt-accent-soft) !important;
    border-top-color: var(--pt-accent) !important;
}

main.route-controller-phaseworkload .workload-toolbar,
main.route-controller-phaseworkload .workload-responsive-wrap {
    scrollbar-color: var(--pt-accent-dark) transparent !important;
}

main.route-controller-phaseworkload :is(.workload-toolbar, .workload-responsive-wrap)::-webkit-scrollbar-track {
    background: transparent !important;
}

main.route-controller-phaseworkload :is(.workload-toolbar, .workload-responsive-wrap)::-webkit-scrollbar-thumb {
    background: var(--pt-accent-dark) !important;
    border-radius: 999px !important;
}

.requirement-card-popup__header {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 8%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
}

.requirement-card-popup__eyebrow,
.requirement-card-popup__meta,
.requirement-card-popup__count,
.requirement-card-popup__phase-pill {
    color: var(--pt-accent) !important;
}
");
            return sb.ToString();
        }

        private static void AppendVar(StringBuilder sb, string name, string value)
        {
            sb.Append("  ")
                .Append(name)
                .Append(": ")
                .Append(WebUtility.HtmlEncode(value))
                .AppendLine(";");
        }

        private static decimal ClampFontScale(decimal value)
        {
            if (value < 0.90m) return 0.90m;
            if (value > 1.15m) return 1.15m;
            return decimal.Round(value, 2);
        }

        private static string NormalizeHexOrDefault(string? value, string fallback)
        {
            value = (value ?? "").Trim();
            if (value.Length == 6 && IsHexDigits(value))
                value = "#" + value;

            return IsHexColor(value) ? value.ToUpperInvariant() : fallback.ToUpperInvariant();
        }

        private static bool IsHexColor(string? value)
        {
            value = (value ?? "").Trim();
            return value.Length == 7 && value[0] == '#' && IsHexDigits(value[1..]);
        }

        private static bool IsHexDigits(string value)
        {
            foreach (var ch in value)
            {
                var isHex = ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
                if (!isHex) return false;
            }

            return true;
        }

        private static string ToRgba(string hex, double alpha)
        {
            var (r, g, b) = ParseHex(hex);
            return string.Format(CultureInfo.InvariantCulture, "rgba({0}, {1}, {2}, {3:0.##})", r, g, b, alpha);
        }

        private static string ShiftHex(string hex, double amount)
        {
            var (r, g, b) = ParseHex(hex);
            int Shift(int channel)
            {
                var target = amount >= 0 ? 255 : 0;
                var shifted = channel + (target - channel) * Math.Abs(amount);
                return Math.Clamp((int)Math.Round(shifted), 0, 255);
            }

            return $"#{Shift(r):X2}{Shift(g):X2}{Shift(b):X2}";
        }

        private static string GetReadableContrast(string hex)
        {
            var (r, g, b) = ParseHex(hex);
            var luminance = (0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b));
            return luminance > 0.55 ? "#062B2F" : "#FFFFFF";
        }

        private static double ToLinear(int channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static (int R, int G, int B) ParseHex(string hex)
        {
            hex = NormalizeHexOrDefault(hex, "#14b8a6").TrimStart('#');
            return (
                int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            );
        }
    }
}
