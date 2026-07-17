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
        private const string DefaultDinoName = "Dino";
        private const string DefaultDinoColorHex = "#FFFFFF";
        private const string DefaultDinoFoodColorHex = "#45D6C6";
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
            var profileBallEnabled = preference?.ProfileBallEnabled ?? false;
            var dinoName = NormalizeDinoName(preference?.DinoName);
            var dinoColor = NormalizeHexOrDefault(preference?.DinoColorHex, DefaultDinoColorHex);
            var dinoFoodColor = NormalizeHexOrDefault(preference?.DinoFoodColorHex, DefaultDinoFoodColorHex);
            var resolved = ResolveTheme(selectedPreset, useCustom, accent, sidebar, bodyBg, chartPanel, fontScale, profileBallEnabled, dinoColor, dinoFoodColor);

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
                ProfileBallEnabled = profileBallEnabled,
                DinoName = dinoName,
                DinoColorHex = dinoColor,
                DinoFoodColorHex = dinoFoodColor,
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
            var dinoName = NormalizeDinoName(model.DinoName);
            var dinoColor = NormalizeHexOrDefault(model.DinoColorHex, DefaultDinoColorHex);
            var dinoFoodColor = NormalizeHexOrDefault(model.DinoFoodColorHex, DefaultDinoFoodColorHex);

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

            if (!IsHexColor(model.DinoColorHex) || !IsHexColor(model.DinoFoodColorHex))
                return (false, "ค่าสีไดโนเสาร์และสีอาหารต้องอยู่ในรูปแบบ #RRGGBB");

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
            preference.ProfileBallEnabled = model.ProfileBallEnabled;
            preference.DinoName = dinoName;
            preference.DinoColorHex = dinoColor;
            preference.DinoFoodColorHex = dinoFoodColor;
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
                    return ResolveTheme(defaultPreset, false, defaultPreset.AccentHex, defaultPreset.SidebarHex, defaultPreset.BodyBgHex, defaultPreset.ChartPanelHex, 1.00m, false, DefaultDinoColorHex, DefaultDinoFoodColorHex);

                var preference = await _context.UserThemePreferences
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == userId.Value, cancellationToken);

                if (preference == null)
                    return ResolveTheme(defaultPreset, false, defaultPreset.AccentHex, defaultPreset.SidebarHex, defaultPreset.BodyBgHex, defaultPreset.ChartPanelHex, 1.00m, false, DefaultDinoColorHex, DefaultDinoFoodColorHex);

                var preset = presets.FirstOrDefault(x => x.ThemeId == preference.ThemeId) ?? defaultPreset;
                var accent = NormalizeHexOrDefault(preference.CustomAccentHex, preset.AccentHex);
                var sidebar = NormalizeHexOrDefault(preference.CustomSidebarHex, preset.SidebarHex);
                var bodyBg = NormalizeHexOrDefault(preference.CustomBodyBgHex, preset.BodyBgHex);
                var chartPanel = NormalizeHexOrDefault(preference.CustomChartPanelHex, preset.ChartPanelHex);
                var dinoColor = NormalizeHexOrDefault(preference.DinoColorHex, DefaultDinoColorHex);
                var dinoFoodColor = NormalizeHexOrDefault(preference.DinoFoodColorHex, DefaultDinoFoodColorHex);
                return ResolveTheme(preset, preference.UseCustom, accent, sidebar, bodyBg, chartPanel, preference.FontScale, preference.ProfileBallEnabled, dinoColor, dinoFoodColor);
            }
            catch
            {
                var fallback = CreateFallbackPreset();
                return ResolveTheme(fallback, false, fallback.AccentHex, fallback.SidebarHex, fallback.BodyBgHex, fallback.ChartPanelHex, 1.00m, false, DefaultDinoColorHex, DefaultDinoFoodColorHex);
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
            AccentHex = "#1F4889",
            AccentDarkHex = "#193B70",
            AccentDeepHex = "#163260",
            SidebarHex = "#081c42",
            SidebarDeepHex = "#031934",
            BodyBgHex = "#eef3f9",
            ChartPanelHex = "#041F4E",
            SurfaceHex = "#ffffff",
            TextHex = "#0f172a",
            MutedHex = "#64748b",
            ContrastHex = "#FFFFFF"
        };

        private static ThemePresetOptionViewModel ToPresetOption(ThemePreset preset) => new()
        {
            ThemeId = preset.ThemeId,
            ThemeKey = preset.ThemeKey,
            ThemeName = preset.ThemeName,
            IsDefault = preset.IsDefault,
            AccentHex = NormalizeHexOrDefault(preset.AccentHex, "#1F4889"),
            AccentDarkHex = NormalizeHexOrDefault(preset.AccentDarkHex, "#193B70"),
            SidebarHex = NormalizeHexOrDefault(preset.SidebarHex, "#081c42"),
            BodyBgHex = NormalizeHexOrDefault(preset.BodyBgHex, "#eef3f9"),
            ChartPanelHex = NormalizeHexOrDefault(preset.ChartPanelHex, NormalizeHexOrDefault(preset.SidebarHex, "#081c42")),
            TextHex = NormalizeHexOrDefault(preset.TextHex, "#0f172a"),
            ContrastHex = NormalizeHexOrDefault(preset.ContrastHex, GetReadableContrast(NormalizeHexOrDefault(preset.AccentHex, "#1F4889")))
        };

        private static ResolvedThemeViewModel ResolveTheme(
            ThemePreset preset,
            bool useCustom,
            string customAccentHex,
            string customSidebarHex,
            string customBodyBgHex,
            string customChartPanelHex,
            decimal fontScale,
            bool profileBallEnabled = false,
            string? dinoColorHex = null,
            string? dinoFoodColorHex = null)
        {
            var accent = useCustom ? customAccentHex : NormalizeHexOrDefault(preset.AccentHex, "#1F4889");
            var sidebar = useCustom ? customSidebarHex : NormalizeHexOrDefault(preset.SidebarHex, "#081c42");
            var bodyBg = useCustom ? customBodyBgHex : NormalizeHexOrDefault(preset.BodyBgHex, "#eef3f9");
            var chartPanel = useCustom ? customChartPanelHex : NormalizeHexOrDefault(preset.ChartPanelHex, NormalizeHexOrDefault(preset.SidebarHex, "#081c42"));
            var accentDark = useCustom ? ShiftHex(accent, -0.18) : NormalizeHexOrDefault(preset.AccentDarkHex, ShiftHex(accent, -0.18));
            var accentDeep = useCustom ? ShiftHex(accent, -0.30) : NormalizeHexOrDefault(preset.AccentDeepHex, ShiftHex(accent, -0.30));
            var chartPanelContrast = GetReadableContrast(chartPanel);
            var dinoColor = NormalizeHexOrDefault(dinoColorHex, DefaultDinoColorHex);
            var dinoFoodColor = NormalizeHexOrDefault(dinoFoodColorHex, DefaultDinoFoodColorHex);

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
                ProfileBallEnabled = profileBallEnabled,
                DinoColorHex = dinoColor,
                DinoFoodColorHex = dinoFoodColor,
                DinoFoodColorSoftRgba = ToRgba(dinoFoodColor, .24),
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
            var bodyContrast = GetReadableContrast(theme.BodyBgHex);
            var surfaceContrast = GetReadableContrast(theme.SurfaceHex);
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
            AppendVar(sb, "--pt-report-head-text", theme.TextHex);
            AppendVar(sb, "--pt-report-head-border", ToRgba(theme.TextHex, .34));
            AppendVar(sb, "--pt-body-bg", theme.BodyBgHex);
            AppendVar(sb, "--pt-body-bg-soft", ShiftHex(theme.BodyBgHex, -0.04));
            AppendVar(sb, "--pt-body-contrast", bodyContrast);
            AppendVar(sb, "--pt-body-contrast-muted", ToRgba(bodyContrast, .72));
            AppendVar(sb, "--pt-chart-panel-bg", theme.ChartPanelHex);
            AppendVar(sb, "--pt-chart-panel-deep", theme.ChartPanelDeepHex);
            AppendVar(sb, "--pt-chart-panel-soft", ToRgba(theme.ChartPanelHex, .88));
            AppendVar(sb, "--pt-chart-panel-center-bg", ShiftHex(theme.ChartPanelHex, -0.10));
            AppendVar(sb, "--pt-chart-panel-field-bg", ToRgba(theme.ChartPanelDeepHex, .72));
            AppendVar(sb, "--pt-chart-panel-row-bg", ShiftHex(theme.ChartPanelHex, -0.08));
            AppendVar(sb, "--pt-chart-panel-row-hover-bg", ShiftHex(theme.ChartPanelHex, 0.04));
            AppendVar(sb, "--pt-chart-panel-glow", ToRgba(theme.AccentHex, .14));
            AppendVar(sb, "--pt-chart-panel-contrast", theme.ChartPanelContrastHex);
            AppendVar(sb, "--pt-chart-panel-contrast-muted", theme.ChartPanelContrastMutedRgba);
            AppendVar(sb, "--pt-surface", theme.SurfaceHex);
            AppendVar(sb, "--pt-surface-soft", ShiftHex(theme.SurfaceHex, -0.04));
            AppendVar(sb, "--pt-surface-contrast", surfaceContrast);
            AppendVar(sb, "--pt-surface-contrast-muted", ToRgba(surfaceContrast, .68));
            AppendVar(sb, "--pt-detail-color", surfaceContrast);
            AppendVar(sb, "--pt-detail-muted", ToRgba(surfaceContrast, .70));
            AppendVar(sb, "--pt-label-color", ToRgba(surfaceContrast, .78));
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
            AppendVar(sb, "--pt-dino-color", theme.DinoColorHex);
            AppendVar(sb, "--pt-dino-food-color", theme.DinoFoodColorHex);
            AppendVar(sb, "--pt-dino-food-soft", theme.DinoFoodColorSoftRgba);
            AppendVar(sb, "--pt-user-font-scale", scale);
            sb.AppendLine("}");
            sb.AppendLine("html { font-size: calc(14px * var(--pt-user-font-scale)); }");
            sb.AppendLine("@media (min-width: 768px) { html { font-size: calc(16px * var(--pt-user-font-scale)); } }");
            sb.AppendLine("body { background: var(--pt-body-bg) !important; color: var(--pt-body-contrast) !important; }");
            if (theme.ProfileBallEnabled)
            {
                sb.AppendLine(".dashboard-view .dashboard-dino-runner { display: flex !important; }");
                sb.AppendLine(".dashboard-view .dashboard-dino-feed-zone { display: block !important; }");
                sb.AppendLine("@media (max-width: 980px) { .dashboard-view :is(.dashboard-dino-runner, .dashboard-dino-feed-zone, .dashboard-dino-food) { display: none !important; } }");
            }
            else
            {
                sb.AppendLine(".dashboard-view :is(.dashboard-dino-runner, .dashboard-dino-feed-zone, .dashboard-dino-food) { display: none !important; }");
            }
            sb.AppendLine(@"
body:not(.pt-standalone-report) .v2-page,
body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) {
    --pt-local-text: var(--pt-body-contrast);
    --pt-local-muted: var(--pt-body-contrast-muted);
    color: var(--pt-local-text) !important;
}

body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) :is(
    .card,
    .card-body,
    .modal-content,
    .list-group-item,
    .table-responsive,
    .filter-panel,
    .filter-card,
    .project-filter-form,
    .phase-filter-form,
    .pt-form-card,
    .form-card,
    .employee-card,
    .followup-card,
    .notification-card,
    .send-log-panel,
    .approval-card,
    .config-card,
    .settings-card,
    .line-overdue-card,
    .project-card,
    .phase-card,
    .support-card,
    .issue-card,
    .weekly-card,
    .report-card,
    .report-section,
    .page-card,
    .content-card
):not(.index-hero):not(.glass-panel):not(.kpi-card):not(.dashboard-card):not(.workload-content-card) {
    --pt-local-text: var(--pt-surface-contrast);
    --pt-local-muted: var(--pt-surface-contrast-muted);
}

body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) :is(
    .index-hero,
    .page-hero,
    .report-hero,
    .weekly-hero,
    .issue-hero,
    .dev-issue-hero,
    .support-hero,
    .support-detail-hero,
    .support-dev-hero,
    .meeting-hero,
    .meeting-form-hero,
    .meeting-detail-hero,
    .workload-calendar-toolbar,
    [class$='-hero']
) {
    --pt-local-text: var(--pt-sidebar-contrast);
    --pt-local-muted: var(--pt-sidebar-contrast-muted);
}

body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) :is(
    h1,
    h2,
    h3,
    h4,
    h5,
    h6,
    p,
    li,
    dt,
    dd,
    td,
    th,
    label,
    .form-label,
    .text-dark,
    .text-body,
    .card-title,
    .section-title,
    [class*='title'],
    [class*='heading'],
    [class*='name'],
    [class*='value'],
    [class*='detail'],
    [class*='content'],
    [class*='number'],
    [class*='count']
):not(.btn):not(.badge):not(.alert):not(.dropdown-item):not(.dropdown-header):not([class*='status']):not([class*='Status']):not([class*='tone']):not([class*='Tone']):not([class*='dot']):not([class*='line']):not([class*='icon']):not([class*='chart']):not([class*='Chart']) {
    color: var(--pt-local-text, var(--pt-body-contrast)) !important;
}

body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) :is(
    .text-muted,
    .text-secondary,
    .small,
    small,
    em,
    time,
    .form-text,
    [class*='muted'],
    [class*='label'],
    [class*='meta'],
    [class*='subtitle'],
    [class*='description'],
    [class*='desc'],
    [class*='note'],
    [class*='help'],
    [class*='hint'],
    [class*='empty']
):not(.btn):not(.badge):not(.alert):not(.dropdown-item):not([class*='status']):not([class*='Status']):not([class*='tone']):not([class*='Tone']):not([class*='dot']):not([class*='line']):not([class*='icon']):not([class*='chart']):not([class*='Chart']) {
    color: var(--pt-local-muted, var(--pt-body-contrast-muted)) !important;
}

body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) :is(
    .form-control,
    .form-select,
    .pt-search-select__input,
    textarea,
    input:not([type='checkbox']):not([type='radio']):not([type='color']):not([type='file']),
    select
) {
    color: var(--pt-surface-contrast) !important;
    -webkit-text-fill-color: var(--pt-surface-contrast) !important;
    background-color: var(--pt-field-bg) !important;
    border-color: var(--pt-border) !important;
}

body:not(.pt-standalone-report) main:not(.route-controller-home):not(.route-controller-meetingroom):not(.route-controller-requirementboard) :is(
    .form-control,
    .form-select,
    .pt-search-select__input,
    textarea,
    input,
    select
)::placeholder {
    color: var(--pt-surface-contrast-muted) !important;
    -webkit-text-fill-color: var(--pt-surface-contrast-muted) !important;
}
");
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
            sb.AppendLine(".navbar.navbar-dark .navbar-nav .nav-link.active-menu { background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important; color: var(--pt-accent-contrast) !important; }");
            sb.AppendLine(".navbar.navbar-dark .navbar-nav .nav-link:hover, .navbar.navbar-dark .navbar-nav .nav-link:focus, .navbar.navbar-dark .navbar-nav .show > .nav-link { background: var(--pt-accent-soft) !important; }");
            sb.AppendLine(".bg-info, .form-check-input:checked { background-color: var(--pt-accent) !important; border-color: var(--pt-accent) !important; color: var(--pt-accent-contrast) !important; }");
            sb.AppendLine(".footer-modern .footer-brand { color: var(--pt-accent-dark) !important; border-color: var(--pt-accent) !important; }");
            sb.AppendLine(".form-control:focus, .form-select:focus, .form-check-input:focus { border-color: var(--pt-accent) !important; box-shadow: 0 0 0 4px var(--pt-accent-soft) !important; }");
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

.v2-sidebar,
.v2-sidebar .v2-profile-name,
.v2-sidebar .v2-profile-name-row,
.v2-sidebar .sidebar-logo-text {
    color: var(--pt-sidebar-contrast) !important;
}

.v2-sidebar .v2-profile-role,
.v2-sidebar .v2-profile-role-row {
    color: var(--pt-sidebar-contrast-muted) !important;
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

html body main .index-hero :is(.index-title, .index-subtitle, .index-eyebrow) {
    color: var(--pt-sidebar-contrast) !important;
    -webkit-text-fill-color: var(--pt-sidebar-contrast) !important;
}

html body main .index-hero :is(.index-subtitle, .index-eyebrow) {
    color: var(--pt-sidebar-contrast-muted) !important;
    -webkit-text-fill-color: var(--pt-sidebar-contrast-muted) !important;
    opacity: 1 !important;
}

.index-subtitle,
.index-eyebrow {
    opacity: .86;
}

main :is(.card, .table-responsive):not(.glass-panel):not(.kpi-card):not(.project-overview-table) {
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
    box-shadow: 0 12px 28px var(--pt-shadow) !important;
}

main :is(.form-control, .form-select, .pt-search-select__input) {
    background-color: var(--pt-field-bg) !important;
    border-color: var(--pt-border) !important;
    color: var(--pt-surface-contrast) !important;
}

main :is(.form-control, .form-select, .pt-search-select__input)::placeholder {
    color: var(--pt-surface-contrast-muted) !important;
}

.pt-search-select__dropdown {
    border-color: var(--pt-border) !important;
    box-shadow: 0 18px 34px var(--pt-shadow) !important;
}

.pt-search-select__option:hover,
.pt-search-select__option:focus,
.pt-search-select__option.is-selected {
    background: var(--pt-accent-soft) !important;
    color: var(--pt-surface-contrast) !important;
}

.dashboard-view {
    --panel: var(--pt-sidebar-bg);
    color: var(--pt-body-contrast) !important;
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
    color: var(--pt-surface-contrast) !important;
    -webkit-text-fill-color: var(--pt-surface-contrast) !important;
    background: linear-gradient(180deg, var(--pt-field-bg), var(--pt-surface-soft)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 12px 24px var(--pt-shadow), inset 0 1px 0 rgba(255,255,255,.68) !important;
}

.dashboard-global-search input:focus {
    border-color: var(--pt-accent) !important;
    box-shadow: 0 14px 30px var(--pt-accent-soft), 0 0 0 3px var(--pt-accent-soft) !important;
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

.dashboard-view .panel-time .time-metric-grid {
    background: transparent !important;
    border-color: transparent !important;
    box-shadow: none !important;
}

.dashboard-view .panel-time :is(.time-goal, .time-trend-card, .time-heatmap-card, .time-metric-grid > span, .time-detail-grid > span) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 18%, var(--pt-accent-soft), transparent 44%),
        linear-gradient(180deg, var(--pt-sidebar-soft), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border) !important;
}

.dashboard-view .panel-time :is(.time-goal span, .time-goal small, .time-goal-trend small, .time-subhead small, .time-trend-bar small, .time-metric-grid span i, .time-detail-grid small) {
    color: var(--pt-sidebar-contrast-muted) !important;
}

.dashboard-view .panel-time :is(.time-subhead b, .time-trend-bar b, .time-metric-grid b, .time-detail-grid b) {
    color: var(--pt-sidebar-contrast) !important;
}

.dashboard-view .panel-time .time-goal b,
.dashboard-view .panel-time .time-metric-grid span:nth-child(1) b {
    color: var(--pt-chart-success) !important;
}

.dashboard-view .panel-time .time-metric-grid span:nth-child(2) b {
    color: var(--pt-chart-primary) !important;
}

.dashboard-view .panel-time .time-metric-grid span:nth-child(3) b {
    color: var(--pt-chart-warning) !important;
}

.dashboard-view .panel-time .time-metric-grid span:nth-child(4) b {
    color: var(--pt-chart-info) !important;
}

.dashboard-view .panel-time .time-goal-trend small.negative,
.dashboard-view .panel-time .time-goal-trend small.danger {
    color: var(--pt-chart-danger) !important;
}

.dashboard-view .panel-time .time-goal-trend small.positive,
.dashboard-view .panel-time .time-goal-trend small.success {
    color: var(--pt-chart-success) !important;
}

.dashboard-view :is(.donut > div, .gauge > div, .project-status-donut > div, .line-overdue-donut > div, .donut-small > div) {
    color: var(--pt-chart-panel-contrast) !important;
    background:
        radial-gradient(circle at 50% 24%, var(--pt-chart-panel-row-hover-bg), transparent 52%),
        linear-gradient(180deg, var(--pt-chart-panel-center-bg), var(--pt-chart-panel-deep)) !important;
    box-shadow: inset 0 0 24px var(--pt-chart-panel-glow), 0 10px 20px var(--pt-shadow) !important;
}

.dashboard-view .time-donut {
    background:
        radial-gradient(circle at 50% 50%, var(--pt-chart-panel-center-bg) 0 47%, transparent 48%),
        var(--donut) !important;
}

.dashboard-view :is(.donut strong, .donut span, .gauge strong, .gauge span, .time-donut strong, .time-donut span) {
    color: var(--pt-chart-panel-contrast) !important;
}

.dashboard-view :is(.overview-pie3d-center, .overview-pie3d-center-core) {
    fill: var(--pt-chart-panel-center-bg) !important;
}

.dashboard-view .overview-pie3d-center-shadow {
    fill: var(--pt-chart-panel-deep) !important;
    opacity: .64 !important;
}

.dashboard-view .overview-pie3d-center-highlight {
    fill: var(--pt-chart-panel-glow) !important;
    opacity: .76 !important;
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

.dashboard-view .project-overview-search input {
    color: var(--pt-chart-panel-contrast) !important;
    -webkit-text-fill-color: var(--pt-chart-panel-contrast) !important;
    caret-color: var(--pt-chart-panel-contrast) !important;
    background:
        radial-gradient(circle at 8% 45%, var(--pt-chart-panel-glow), transparent 38%),
        linear-gradient(180deg, var(--pt-chart-panel-field-bg), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.10), 0 10px 20px var(--pt-shadow) !important;
}

.dashboard-view .project-overview-search input::placeholder,
.dashboard-view .project-overview-search i {
    color: var(--pt-chart-panel-contrast-muted) !important;
    -webkit-text-fill-color: var(--pt-chart-panel-contrast-muted) !important;
}

.dashboard-view .project-overview-search input::-webkit-search-cancel-button {
    filter: none !important;
    opacity: .65 !important;
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
.dashboard-view .dot.cyan { color: var(--pt-chart-info) !important; background: currentColor !important; }
.dashboard-view .dot.lime { color: #8cff3f !important; background: currentColor !important; }
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

.dashboard-view :is(.project-status-donut-layout, .project-status-inline, .overview-mini, .panel-issues, .panel-line-overdue) .metric-list > span {
    color: var(--pt-chart-panel-contrast) !important;
    border-color: var(--pt-border) !important;
    background:
        radial-gradient(circle at 14% 30%, var(--pt-chart-panel-glow), transparent 42%),
        linear-gradient(180deg, var(--pt-chart-panel-field-bg), var(--pt-chart-panel-deep)) !important;
}

.dashboard-view .task-summary-card {
    color: var(--pt-chart-panel-contrast) !important;
    background:
        radial-gradient(circle at 14% 30%, var(--pt-chart-panel-glow), transparent 42%),
        linear-gradient(180deg, var(--pt-chart-panel-field-bg), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border) !important;
    box-shadow: inset 0 1px 0 rgba(255,255,255,.10), 0 12px 24px var(--pt-shadow) !important;
}

.dashboard-view .task-summary-card b {
    color: var(--pt-chart-panel-contrast) !important;
}

.dashboard-view .task-summary-card.completed strong {
    color: var(--pt-chart-panel-deep) !important;
    background: var(--pt-chart-success) !important;
}

.dashboard-view .task-summary-card.progress strong {
    color: var(--pt-chart-panel-deep) !important;
    background: var(--pt-chart-primary) !important;
}

.dashboard-view .issues-overview-foot {
    color: var(--pt-chart-panel-contrast-muted) !important;
    background: linear-gradient(180deg, var(--pt-chart-panel-field-bg), var(--pt-chart-panel-deep)) !important;
    border-color: var(--pt-border) !important;
}

html body main .dashboard-view :is(
    .project-overview-title-row h2,
    .task-progress-title h2,
    .issues-overview-head h2,
    .dashboard-card-title h2,
    .overview-mini-head h3,
    .project-status-card-title,
    .metric-name,
    .metric-list b,
    .task-summary-card b,
    .owner-overview-member b,
    .yearly-legend,
    .yearly-legend span,
    .workload-row .workload-name,
    .workload-row .workload-name strong,
    .workload-row b,
    .activity-row time,
    .activity-row time strong,
    .activity-row b,
    .meeting-row b
) {
    color: var(--pt-chart-panel-contrast) !important;
    -webkit-text-fill-color: var(--pt-chart-panel-contrast) !important;
}

html body main .dashboard-view :is(
    .project-overview-title-row small,
    .task-progress-title small,
    .issues-overview-head small,
    .dashboard-card-title em,
    .overview-mini-head small,
    .project-status-updated,
    .owner-overview-y-axis,
    .owner-overview-y-title,
    .owner-overview-member small,
    .owner-overview-footer,
    .dashboard-section-foot,
    .issues-overview-foot,
    .yearly-y-title,
    .yearly-x-title,
    .yearly-y-axis,
    .yearly-y-axis span,
    .yearly-x-axis,
    .yearly-x-axis span,
    .workload-row .workload-name small,
    .activity-row time small,
    .meeting-row small,
    .activity-row small,
    .activity-row em,
    .time-detail-grid small
) {
    color: var(--pt-chart-panel-contrast-muted) !important;
    -webkit-text-fill-color: var(--pt-chart-panel-contrast-muted) !important;
}

html body main .dashboard-view :is(
    .project-overview-title-row h2,
    .task-progress-title h2,
    .issues-overview-head h2,
    .dashboard-card-title h2,
    .line-overdue-title-row h2
) {
    color: var(--pt-sidebar-contrast) !important;
    -webkit-text-fill-color: var(--pt-sidebar-contrast) !important;
}

html body main .dashboard-view :is(
    .project-overview-title-row small,
    .task-progress-title small,
    .issues-overview-head small,
    .dashboard-card-title em,
    .dashboard-card-title h2 small,
    .line-overdue-title-row small
) {
    color: var(--pt-sidebar-contrast-muted) !important;
    -webkit-text-fill-color: var(--pt-sidebar-contrast-muted) !important;
}

html body main .dashboard-view :is(
    .overview-mini,
    .yearly-chart,
    .project-status-donut-layout,
    .owner-overview-chart,
    .owner-overview-member,
    .owner-overview-footer,
    .metric-card,
    .metric-list > span,
    .task-summary-card,
    .issues-overview-foot,
    .workload-row,
    .activity-row,
    .meeting-row,
    .time-detail-grid > span
) {
    color: var(--pt-chart-panel-contrast) !important;
}

html body main .dashboard-view :is(
    .overview-pie3d-total,
    .overview-pie3d-caption,
    .overview-pie3d-labels text
) {
    fill: var(--pt-chart-panel-contrast) !important;
    stroke: var(--pt-chart-panel-center-bg) !important;
}

html body main .dashboard-view .overview-pie3d-caption {
    fill: var(--pt-chart-panel-contrast-muted) !important;
}

html body main .dashboard-view .bar-chart::before {
    border-color: var(--pt-chart-panel-contrast-muted) !important;
    background: repeating-linear-gradient(to bottom, var(--pt-border) 0 1px, transparent 1px 38px) !important;
}

html body main .dashboard-view .workload-rank {
    color: var(--pt-chart-panel-contrast) !important;
    background: var(--pt-chart-success-soft) !important;
    border-color: var(--pt-chart-success) !important;
    box-shadow: 0 0 14px var(--pt-chart-success-soft) !important;
}

.dashboard-view :is(.workload-row, .activity-row, .meeting-row) {
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
.dashboard-view .time-pill.blue {
    background: linear-gradient(180deg, var(--pt-chart-primary-light), var(--pt-chart-primary-dark)) !important;
}

.dashboard-view .progress-track .lime {
    background: linear-gradient(180deg, #b9ff6a, #63d32f) !important;
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
    color: var(--pt-surface-contrast) !important;
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

main.route-controller-meetings .calendar-card__body {
    background: var(--pt-body-bg-soft) !important;
}

main.route-controller-meetings .fc {
    color: var(--pt-surface-contrast) !important;
}

main.route-controller-meetings .fc .fc-toolbar-title,
main.route-controller-meetings .fc .fc-daygrid-day-number,
main.route-controller-meetings :is(.meeting-project-text, .meeting-desc, .meeting-detail-value, .meeting-project-name) {
    color: var(--pt-surface-contrast) !important;
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
    color: var(--pt-surface-contrast) !important;
    background: linear-gradient(180deg, var(--meeting-event-bg-start), var(--meeting-event-bg-end)) !important;
    border-color: var(--meeting-event-border) !important;
    box-shadow: 0 8px 18px var(--pt-shadow) !important;
}

main.route-controller-meetings :is(.fc-event-title, .meeting-event-title, .meeting-event-subtitle, .meeting-event-meta) {
    color: var(--pt-surface-contrast) !important;
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

html body main.route-controller-meetings:is(.route-action-create, .route-action-edit, .route-action-show) {
    color: var(--pt-body-contrast) !important;
}

html body main.route-controller-meetings:is(.route-action-create, .route-action-edit, .route-action-show) :is(
    .meeting-card,
    .meeting-card__header,
    .meeting-card__body,
    .meeting-detail-card,
    .meeting-detail-card__header,
    .meeting-detail-card__body,
    .meeting-detail-item,
    .meeting-attendee-card,
    .meeting-attendee-picker,
    .meeting-selected-attendee,
    .meeting-selected-attendee > span,
    .meeting-attendee-empty,
    .section-title,
    .section-subtitle,
    .form-hint,
    .form-label,
    label,
    .meeting-detail-label,
    .meeting-detail-value,
    .meeting-project-text,
    .meeting-project-name,
    .text-muted
) {
    color: var(--pt-surface-contrast) !important;
}

html body main.route-controller-meetings:is(.route-action-create, .route-action-edit, .route-action-show) :is(
    .meeting-hero,
    .meeting-form-hero,
    .meeting-detail-hero
) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 10%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

html body main.route-controller-meetings:is(.route-action-create, .route-action-edit, .route-action-show) :is(
    .meeting-hero,
    .meeting-form-hero,
    .meeting-detail-hero
) :is(.meeting-hero__title, .meeting-hero__subtitle, .meeting-detail-title, .meeting-detail-subtitle, .index-eyebrow, .index-title, .index-subtitle) {
    color: var(--pt-sidebar-contrast) !important;
}

html body main.route-controller-meetings:is(.route-action-create, .route-action-edit, .route-action-show) :is(
    .form-control,
    .form-select,
    input:not([type='checkbox']):not([type='radio']):not([type='color']):not([type='file']),
    textarea,
    select,
    option
) {
    color: var(--pt-surface-contrast) !important;
    -webkit-text-fill-color: var(--pt-surface-contrast) !important;
    background-color: var(--pt-field-bg) !important;
    border-color: var(--pt-border) !important;
}

html body main.route-controller-meetings.route-action-show .meeting-detail-actions.index-actions :is(.btn, button.btn, a.btn) {
    --pt-action-color: var(--pt-surface-contrast);
    --pt-action-bg: var(--pt-surface);
    --pt-action-border: var(--pt-border);
    color: var(--pt-action-color) !important;
    background: var(--pt-action-bg) !important;
    border-color: var(--pt-action-border) !important;
    box-shadow: 0 8px 18px var(--pt-shadow) !important;
}

html body main.route-controller-meetings.route-action-show .meeting-detail-actions.index-actions :is(.btn-primary, .btn-success) {
    --pt-action-color: var(--pt-accent-contrast);
    --pt-action-bg: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark));
    --pt-action-border: transparent;
}

html body main.route-controller-meetings.route-action-show .meeting-detail-actions.index-actions .btn-danger {
    --pt-action-color: #ffffff;
    --pt-action-bg: #e11d48;
    --pt-action-border: transparent;
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

main.route-controller-projectissues :is(.issue-git-table, .dev-git-table) thead,
main.route-controller-projectissues :is(.issue-git-table, .dev-git-table) thead th {
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border) !important;
}

main:is(.route-controller-supportorders, .route-controller-supportordersdev) :is(.index-hero, .support-hero, .support-detail-hero, .support-dev-hero) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 10%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
    box-shadow: 0 18px 40px var(--pt-shadow), inset 0 1px 0 rgba(255,255,255,.10) !important;
}

main:is(.route-controller-supportorders, .route-controller-supportordersdev) :is(.index-hero, .support-hero, .support-detail-hero, .support-dev-hero) :is(.index-title, .index-eyebrow, .support-title, .support-subtitle, .support-detail-title, .support-eyebrow, .support-dev-title, .support-dev-eyebrow) {
    color: var(--pt-sidebar-contrast) !important;
}

main:is(.route-controller-supportorders, .route-controller-supportordersdev) :is(.index-hero, .support-hero, .support-detail-hero, .support-dev-hero) :is(.index-subtitle, .support-detail-subtitle, .support-dev-subtitle) {
    color: var(--pt-sidebar-contrast-muted) !important;
}

main:is(.route-controller-supportorders, .route-controller-supportordersdev) :is(.support-git-table, .support-dev-git-table, .dev-git-table) thead,
main:is(.route-controller-supportorders, .route-controller-supportordersdev) :is(.support-git-table, .support-dev-git-table, .dev-git-table) thead th {
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border) !important;
}

html body main:is(
    .route-controller-projectissues,
    .route-controller-supportorders,
    .route-controller-supportordersdev
) {
    color: var(--pt-body-contrast) !important;
}

html body main:is(
    .route-controller-projectissues,
    .route-controller-supportorders,
    .route-controller-supportordersdev
) :is(
    .issue-card,
    .dev-issue-card,
    .support-order-card,
    .support-dev-card,
    .issue-panel,
    .dev-panel,
    .support-panel,
    .support-dev-panel,
    .issue-summary-card,
    .dev-summary-card,
    .support-summary-card,
    .support-dev-summary-card,
    .issue-text-box,
    .dev-text-box,
    .support-text-box,
    .support-dev-text-box,
    .issue-info-block,
    .dev-info-block,
    .support-info-block,
    .support-dev-info-block,
    .issue-help-box,
    .issue-edit-note,
    .dev-readonly,
    .support-dev-readonly,
    .support-dev-info,
    .issue-empty,
    .dev-empty,
    .support-empty,
    .support-dev-empty
) {
    --pt-local-text: var(--pt-chart-panel-contrast);
    --pt-local-muted: var(--pt-chart-panel-contrast-muted);
    color: var(--pt-local-text) !important;
}

html body main:is(
    .route-controller-projectissues,
    .route-controller-supportorders,
    .route-controller-supportordersdev
) .pt-form-card {
    --pt-local-text: var(--pt-surface-contrast);
    --pt-local-muted: var(--pt-surface-contrast-muted);
    color: var(--pt-local-text) !important;
}

html body main:is(
    .route-controller-projectissues,
    .route-controller-supportorders,
    .route-controller-supportordersdev
) :is(
    .issue-title,
    .issue-detail,
    .issue-view-detail,
    .issue-meta,
    .issue-view-meta,
    .issue-card-people,
    .issue-card-person,
    .issue-info-line,
    .issue-info-label,
    .issue-info-value,
    .issue-summary-label,
    .issue-summary-value,
    .issue-status-item,
    .issue-status-row,
    .issue-count,
    .dev-title,
    .dev-detail-title,
    .dev-issue-name,
    .dev-issue-detail,
    .dev-issue-meta,
    .dev-issue-people,
    .dev-issue-person,
    .dev-info-line,
    .dev-info-label,
    .dev-info-value,
    .dev-summary-label,
    .dev-summary-value,
    .dev-status-row,
    .dev-count,
    .support-title,
    .support-detail-title,
    .support-order-title,
    .support-project-name,
    .support-detail,
    .support-meta,
    .support-meta-text,
    .support-card-people,
    .support-card-person,
    .support-info-line,
    .support-info-label,
    .support-info-value,
    .support-summary-label,
    .support-summary-value,
    .support-status-row,
    .support-count,
    .support-dev-title,
    .support-dev-edit-title,
    .support-dev-order-title,
    .support-dev-project,
    .support-dev-detail,
    .support-dev-meta,
    .support-dev-pill,
    .support-dev-card-people,
    .support-dev-card-person,
    .support-dev-info-line,
    .support-dev-info-label,
    .support-dev-info-value,
    .support-dev-summary-label,
    .support-dev-summary-value,
    .support-dev-status-row,
    .support-dev-count,
    .issue-panel h3,
    .dev-panel h3,
    .support-panel h3,
    .support-dev-panel h3,
    .issue-gallery-head h3,
    .dev-gallery-head h3,
    .support-gallery-head h3,
    .support-dev-gallery-head h3,
    .issue-detail-text-block h4,
    .dev-detail-text-block h4,
    .support-detail-text-block h4,
    .support-dev-detail-text-block h4,
    .issue-edit-section-title,
    .status-text
) {
    color: var(--pt-local-text, var(--pt-chart-panel-contrast)) !important;
}

html body main:is(
    .route-controller-projectissues,
    .route-controller-supportorders,
    .route-controller-supportordersdev
) :is(
    .issue-eyebrow,
    .dev-eyebrow,
    .support-eyebrow,
    .support-dev-eyebrow,
    .issue-summary-card small,
    .dev-summary-card small,
    .support-summary-card small,
    .support-dev-summary-card small
) {
    color: var(--pt-local-muted, var(--pt-chart-panel-contrast-muted)) !important;
}

main:is(.route-controller-phaseassigns, .route-controller-projectphases) :is(.assign-report-table, .phase-report-table) thead,
main:is(.route-controller-phaseassigns, .route-controller-projectphases) :is(.assign-report-table, .phase-report-table) thead th {
    color: var(--pt-report-head-text) !important;
    background: transparent !important;
    background-color: transparent !important;
    border-color: var(--pt-report-head-border) !important;
    border-width: 2px !important;
    box-shadow: inset 0 -2px 0 var(--pt-report-head-border) !important;
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

main:is(.route-controller-reports, .route-controller-weeklyreports, .route-controller-assignedemployeesreport, .route-controller-statusapprovals, .route-controller-projectstatus) table thead,
main:is(.route-controller-reports, .route-controller-weeklyreports, .route-controller-assignedemployeesreport, .route-controller-statusapprovals, .route-controller-projectstatus) table thead th {
    --bs-table-bg: transparent !important;
    --bs-table-color: var(--pt-report-head-text) !important;
    color: var(--pt-report-head-text) !important;
    background: transparent !important;
    background-color: transparent !important;
    border-color: var(--pt-report-head-border) !important;
    border-width: 2px !important;
    box-shadow: inset 0 -2px 0 var(--pt-report-head-border) !important;
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
    color: var(--pt-report-head-text) !important;
    background: transparent !important;
    background-color: transparent !important;
    border-color: var(--pt-report-head-border) !important;
    border-width: 2px !important;
    box-shadow: inset 0 -2px 0 var(--pt-report-head-border) !important;
}

@media print {
    body.pt-standalone-report {
        color: #10233f !important;
        background: #fff !important;
    }
}

main.route-controller-phaseworkload {
    color: var(--pt-body-contrast) !important;
    background:
        radial-gradient(circle at 18% 12%, var(--pt-accent-soft), transparent 26%),
        linear-gradient(180deg, var(--pt-body-bg), var(--pt-body-bg-soft)) !important;
}

main.route-controller-phaseworkload .workload-calendar-card {
    color: var(--pt-surface-contrast) !important;
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
    color: var(--pt-surface-contrast) !important;
    background: var(--pt-field-bg) !important;
    border: 1px solid var(--pt-border) !important;
    box-shadow: 0 12px 26px var(--pt-shadow) !important;
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

html body main.route-controller-meetings {
    color: var(--pt-body-contrast) !important;
    background: var(--pt-body-bg) !important;
}

html body main.route-controller-meetings :is(.meeting-page, .meeting-create-wrap) {
    color: var(--pt-body-contrast) !important;
}

html body main.route-controller-meetings :is(
    .meeting-header,
    .meeting-hero,
    .meeting-form-hero,
    .meeting-detail-hero,
    .calendar-card__toolbar
) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 10%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

html body main.route-controller-meetings :is(
    .meeting-title,
    .meeting-subtitle,
    .meeting-hero__title,
    .meeting-hero__subtitle,
    .meeting-detail-title,
    .meeting-detail-subtitle,
    .calendar-heading__eyebrow,
    .calendar-heading__title,
    .calendar-heading__subtitle,
    .calendar-heading__pill,
    .calendar-card__toolbar .text-muted
) {
    color: var(--pt-sidebar-contrast) !important;
}

html body main.route-controller-meetings :is(
    .calendar-card,
    .meeting-list-card,
    .meeting-card,
    .meeting-detail-card,
    .meeting-card__header,
    .meeting-card__body,
    .meeting-detail-card__header,
    .meeting-detail-card__body,
    .meeting-detail-item,
    .meeting-attendee-card,
    .meeting-attendee-picker,
    .meeting-detail-title-card,
    .meeting-detail-summary-block,
    .meeting-detail-description-card,
    .meeting-detail-meta-card
) {
    color: var(--pt-surface-contrast) !important;
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
}

html body main.route-controller-meetings :is(
    .calendar-card__body,
    .meeting-list-card .card-body,
    .meeting-selected-attendee,
    .meeting-attendee-empty
) {
    color: var(--pt-surface-contrast) !important;
    background: var(--pt-surface-soft) !important;
    border-color: var(--pt-border) !important;
}

html body main.route-controller-meetings :is(
    .calendar-heading__date,
    .fc .fc-header-toolbar,
    .fc .fc-scrollgrid,
    .fc .fc-list,
    .fc .fc-daygrid-day,
    .fc .fc-daygrid-day.fc-day-sun,
    .fc .fc-daygrid-day.fc-day-mon,
    .fc .fc-daygrid-day.fc-day-tue,
    .fc .fc-daygrid-day.fc-day-wed,
    .fc .fc-daygrid-day.fc-day-thu,
    .fc .fc-daygrid-day.fc-day-fri,
    .fc .fc-daygrid-day.fc-day-sat,
    .fc .fc-timegrid-axis,
    .fc .fc-timegrid-slot-label,
    .fc .fc-timegrid-slot,
    .fc .fc-timegrid-col
) {
    color: var(--pt-surface-contrast) !important;
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
}

html body main.route-controller-meetings :is(
    .fc .fc-daygrid-day.fc-day-other,
    .fc .fc-list-event:hover td,
    .fc .fc-timegrid-col.fc-day-today
) {
    color: var(--pt-surface-contrast-muted) !important;
    background: var(--pt-surface-soft) !important;
}

html body main.route-controller-meetings :is(
    .fc .fc-col-header-cell,
    .calendar-card .fc .fc-col-header-cell,
    .fc .fc-list-day-cushion,
    .meeting-list-card .card-header
) {
    color: var(--pt-sidebar-contrast) !important;
    background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

html body main.route-controller-meetings :is(
    .fc .fc-col-header-cell .fc-col-header-cell-cushion,
    .calendar-card .fc .fc-col-header-cell .fc-col-header-cell-cushion,
    .fc .fc-day-head-weekday,
    .fc .fc-day-head-date
) {
    color: var(--pt-sidebar-contrast) !important;
    text-shadow: none !important;
}

html body main.route-controller-meetings :is(
    .fc .fc-toolbar-title,
    .fc .fc-daygrid-day-number,
    .fc-day-sun .fc-daygrid-day-number,
    .fc-day-mon .fc-daygrid-day-number,
    .fc-day-tue .fc-daygrid-day-number,
    .fc-day-wed .fc-daygrid-day-number,
    .fc-day-thu .fc-daygrid-day-number,
    .fc-day-fri .fc-daygrid-day-number,
    .fc-day-sat .fc-daygrid-day-number,
    .fc .fc-list-event td,
    .fc .fc-timegrid-slot-label-cushion,
    .section-title,
    .meeting-project-text,
    .meeting-project-name,
    .meeting-desc,
    .meeting-detail-value,
    .meeting-selected-attendee > span
) {
    color: var(--pt-surface-contrast) !important;
}

html body main.route-controller-meetings :is(
    .section-subtitle,
    .form-hint,
    .meeting-detail-label,
    .meeting-event-subtitle,
    .meeting-event-meta,
    .meeting-location,
    .meeting-time-field > span,
    .text-muted
) {
    color: var(--pt-surface-contrast-muted) !important;
}

html body main.route-controller-meetings .fc .fc-button-primary,
html body main.route-controller-meetings .calendar-heading__action.primary {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    border-color: transparent !important;
}

html body main.route-controller-meetings .calendar-heading__action:not(.primary),
html body main.route-controller-meetings.route-action-show .meeting-detail-actions.index-actions :is(.btn, button.btn, a.btn) {
    color: var(--pt-sidebar-contrast) !important;
    background: var(--pt-accent-soft) !important;
    border-color: var(--pt-border-strong) !important;
}

html body main.route-controller-meetings :is(.fc-event, .meeting-row) {
    --meeting-event-bg-start: var(--pt-field-bg);
    --meeting-event-bg-end: var(--pt-surface-soft);
    --meeting-event-border: var(--pt-border);
    color: var(--pt-surface-contrast) !important;
    background: linear-gradient(180deg, var(--meeting-event-bg-start), var(--meeting-event-bg-end)) !important;
    border-color: var(--meeting-event-border) !important;
}

html body main.route-controller-meetings :is(.fc-event-title, .meeting-event-title) {
    color: var(--pt-surface-contrast) !important;
}

html body main.route-controller-meetings :is(
    .form-control,
    .form-select,
    input:not([type='checkbox']):not([type='radio']):not([type='color']):not([type='file']),
    textarea,
    select,
    option
) {
    color: var(--pt-surface-contrast) !important;
    -webkit-text-fill-color: var(--pt-surface-contrast) !important;
    background-color: var(--pt-field-bg) !important;
    border-color: var(--pt-border) !important;
}

body:has(main.route-controller-requirementboard),
body:has(main.route-controller-requirementboard) .v2-shell,
body:has(main.route-controller-requirementboard) .v2-page,
body:has(main.route-controller-requirementboard) .v2-shell-requirement-board {
    color: var(--pt-body-contrast) !important;
    background: var(--pt-body-bg) !important;
}

html body main.route-controller-requirementboard,
html body main.route-controller-requirementboard .boards-page,
html body main.route-controller-requirementboard .rb-page,
html body main.route-controller-requirementboard .v2-shell-requirement-board {
    color: var(--pt-body-contrast) !important;
}

html body main.route-controller-requirementboard :is(
    .index-hero,
    .rb-hero,
    .rb-board-strip,
    .rb-modal .modal-header:not(.has-cover)
) {
    color: var(--pt-sidebar-contrast) !important;
    background:
        radial-gradient(circle at 88% 8%, var(--pt-accent-glow), transparent 30%),
        linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important;
    border-color: var(--pt-border-strong) !important;
}

html body main.route-controller-requirementboard :is(
    .index-eyebrow,
    .index-title,
    .index-subtitle,
    .rb-eyebrow,
    .rb-hero h1,
    .rb-hero p,
    .rb-stat,
    .rb-stat strong,
    .rb-stat span,
    .rb-boards-btn,
    .rb-boards-btn span:first-child,
    .rb-modal .modal-header:not(.has-cover) :is(.modal-title, h1, h2, h3, h4, h5, h6, p, span)
) {
    color: var(--pt-sidebar-contrast) !important;
}

html body main.route-controller-requirementboard :is(
    .boards-sidebar,
    .boards-section,
    .boards-nav-link,
    .boards-create-summary,
    .boards-group-link,
    .boards-create-box,
    .boards-online-users,
    .boards-stat,
    .boards-rename summary,
    .boards-rename-panel,
    .boards-create-panel,
    .boards-card,
    .boards-create-board,
    .rb-column,
    .rb-add-list-column,
    .rb-add-list-column form,
    .rb-card,
    .rb-modal .modal-content,
    .rb-modal .modal-body,
    .rb-file-upload-card,
    .rb-file-row,
    .rb-phase-draft,
    .rb-description-view,
    .rb-description-editor,
    .rb-description-rich,
    .rb-label-picker,
    .rb-label-panel,
    .rb-label-option
) {
    color: var(--pt-surface-contrast) !important;
    background: var(--pt-surface) !important;
    border-color: var(--pt-border) !important;
}

html body main.route-controller-requirementboard :is(
    .boards-search,
    .boards-input,
    .rb-search-input,
    .rb-input,
    .rb-textarea,
    .rb-phase-row .form-control,
    .rb-date-input-wrap .form-control,
    .rb-file-upload-card .form-control,
    .rb-description-font-size-button,
    .rb-description-font-size-menu,
    .rb-description-color-menu
) {
    color: var(--pt-surface-contrast) !important;
    -webkit-text-fill-color: var(--pt-surface-contrast) !important;
    background: var(--pt-field-bg) !important;
    border-color: var(--pt-border) !important;
}

html body main.route-controller-requirementboard :is(
    .rb-description-placeholder
) {
    color: var(--pt-surface-contrast-muted) !important;
    opacity: .78 !important;
}

html body main.route-controller-requirementboard .boards-search::placeholder,
html body main.route-controller-requirementboard .boards-input::placeholder,
html body main.route-controller-requirementboard .rb-search-input::placeholder,
html body main.route-controller-requirementboard .rb-input::placeholder,
html body main.route-controller-requirementboard .rb-textarea::placeholder,
html body main.route-controller-requirementboard .rb-phase-row .form-control::placeholder {
    color: var(--pt-surface-contrast-muted) !important;
    opacity: .78 !important;
}

html body main.route-controller-requirementboard :is(
    .boards-sidebar-title,
    .boards-group-meta,
    .boards-section-subtitle,
    .boards-card-meta,
    .boards-online-more,
    .boards-search-icon,
    .rb-search-icon,
    .rb-column-count,
    .rb-column-drag-hint,
    .rb-card-detail,
    .rb-card-meta,
    .rb-file-info,
    .rb-description-title,
    .rb-field-label,
    .rb-save-status
) {
    color: var(--pt-surface-contrast-muted) !important;
}

html body main.route-controller-requirementboard :is(
    .boards-nav-link,
    .boards-create-summary,
    .boards-group-link,
    .boards-section-title,
    .boards-card-title,
    .boards-picker-title,
    .rb-column-title,
    .rb-card-title,
    .rb-file-name,
    .rb-description-view,
    .rb-description-view h3,
    .rb-description-rich,
    .rb-description-rich h3,
    .rb-modal :is(.modal-title, h1, h2, h3, h4, h5, h6, label, strong)
) {
    color: var(--pt-surface-contrast) !important;
}

html body main.route-controller-requirementboard :is(
    .boards-button,
    .rb-btn:not(.secondary):not(.danger),
    .rb-dashboard-btn,
    .rb-add-card-action
) {
    color: var(--pt-accent-contrast) !important;
    background: linear-gradient(135deg, var(--pt-menu-accent), var(--pt-menu-accent-dark)) !important;
    border-color: transparent !important;
}

html body main.route-controller-requirementboard :is(
    .boards-button.secondary,
    .boards-close-button,
    .boards-more-button,
    .rb-btn.secondary,
    .rb-search-clear,
    .rb-description-tool,
    .rb-description-font-size-option,
    .rb-description-color-auto
) {
    color: var(--pt-surface-contrast) !important;
    background: var(--pt-surface-soft) !important;
    border-color: var(--pt-border) !important;
}

html body main.route-controller-requirementboard :is(.boards-danger-button, .rb-btn.danger) {
    color: #ffffff !important;
    background: #e11d48 !important;
    border-color: transparent !important;
}

html body main.route-controller-requirementboard :is(
    .boards-card-cover,
    .boards-image-swatch,
    .boards-color-visual,
    .rb-card-cover-frame,
    .rb-card-cover,
    .rb-card-label,
    .rb-label-color,
    .rb-description-color-option
) {
    forced-color-adjust: none;
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

        private static string NormalizeDinoName(string? value)
        {
            var name = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return DefaultDinoName;

            return name.Length <= 24 ? name : name[..24];
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
            hex = NormalizeHexOrDefault(hex, "#1F4889").TrimStart('#');
            return (
                int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            );
        }
    }
}
