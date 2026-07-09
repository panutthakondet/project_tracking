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
            var fontScale = ClampFontScale(preference?.FontScale ?? 1.00m);
            var resolved = ResolveTheme(selectedPreset, useCustom, accent, sidebar, bodyBg, fontScale);

            return new AppearanceViewModel
            {
                Presets = presets.Select(ToPresetOption).ToList(),
                ThemeId = selectedPreset.ThemeId,
                UseCustom = useCustom,
                CustomAccentHex = accent,
                CustomSidebarHex = sidebar,
                CustomBodyBgHex = bodyBg,
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

            if (model.UseCustom)
            {
                if (!IsHexColor(model.CustomAccentHex) ||
                    !IsHexColor(model.CustomSidebarHex) ||
                    !IsHexColor(model.CustomBodyBgHex))
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
                    return ResolveTheme(defaultPreset, false, defaultPreset.AccentHex, defaultPreset.SidebarHex, defaultPreset.BodyBgHex, 1.00m);

                var preference = await _context.UserThemePreferences
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == userId.Value, cancellationToken);

                if (preference == null)
                    return ResolveTheme(defaultPreset, false, defaultPreset.AccentHex, defaultPreset.SidebarHex, defaultPreset.BodyBgHex, 1.00m);

                var preset = presets.FirstOrDefault(x => x.ThemeId == preference.ThemeId) ?? defaultPreset;
                var accent = NormalizeHexOrDefault(preference.CustomAccentHex, preset.AccentHex);
                var sidebar = NormalizeHexOrDefault(preference.CustomSidebarHex, preset.SidebarHex);
                var bodyBg = NormalizeHexOrDefault(preference.CustomBodyBgHex, preset.BodyBgHex);
                return ResolveTheme(preset, preference.UseCustom, accent, sidebar, bodyBg, preference.FontScale);
            }
            catch
            {
                var fallback = CreateFallbackPreset();
                return ResolveTheme(fallback, false, fallback.AccentHex, fallback.SidebarHex, fallback.BodyBgHex, 1.00m);
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
            TextHex = NormalizeHexOrDefault(preset.TextHex, "#0f172a"),
            ContrastHex = NormalizeHexOrDefault(preset.ContrastHex, GetReadableContrast(NormalizeHexOrDefault(preset.AccentHex, "#14b8a6")))
        };

        private static ResolvedThemeViewModel ResolveTheme(
            ThemePreset preset,
            bool useCustom,
            string customAccentHex,
            string customSidebarHex,
            string customBodyBgHex,
            decimal fontScale)
        {
            var accent = useCustom ? customAccentHex : NormalizeHexOrDefault(preset.AccentHex, "#14b8a6");
            var sidebar = useCustom ? customSidebarHex : NormalizeHexOrDefault(preset.SidebarHex, "#081c42");
            var bodyBg = useCustom ? customBodyBgHex : NormalizeHexOrDefault(preset.BodyBgHex, "#eef3f9");
            var accentDark = useCustom ? ShiftHex(accent, -0.18) : NormalizeHexOrDefault(preset.AccentDarkHex, ShiftHex(accent, -0.18));
            var accentDeep = useCustom ? ShiftHex(accent, -0.30) : NormalizeHexOrDefault(preset.AccentDeepHex, ShiftHex(accent, -0.30));

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
            AppendVar(sb, "--pt-body-bg", theme.BodyBgHex);
            AppendVar(sb, "--pt-surface", theme.SurfaceHex);
            AppendVar(sb, "--pt-text", theme.TextHex);
            AppendVar(sb, "--pt-muted", theme.MutedHex);
            AppendVar(sb, "--pt-user-font-scale", scale);
            sb.AppendLine("}");
            sb.AppendLine("html { font-size: calc(14px * var(--pt-user-font-scale)); }");
            sb.AppendLine("@media (min-width: 768px) { html { font-size: calc(16px * var(--pt-user-font-scale)); } }");
            sb.AppendLine("body { background: var(--pt-body-bg) !important; color: var(--pt-text) !important; }");
            sb.AppendLine(".navbar, footer.footer-modern { background: linear-gradient(135deg, var(--pt-sidebar-bg), var(--pt-sidebar-deep)) !important; }");
            sb.AppendLine("::-webkit-scrollbar-track { background: var(--pt-sidebar-bg) !important; }");
            sb.AppendLine("::-webkit-scrollbar-thumb { background: var(--pt-accent-dark) !important; border-color: var(--pt-sidebar-bg) !important; }");
            sb.AppendLine(".navbar.navbar-dark .navbar-nav .nav-link.active-menu, .btn-primary, .system-update-ack { background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important; color: var(--pt-accent-contrast) !important; }");
            sb.AppendLine(".navbar.navbar-dark .navbar-nav .nav-link:hover, .navbar.navbar-dark .navbar-nav .nav-link:focus, .navbar.navbar-dark .navbar-nav .show > .nav-link { background: var(--pt-accent-soft) !important; }");
            sb.AppendLine(".btn-info, .bg-info, .form-check-input:checked, .active > .page-link, .page-link.active { background-color: var(--pt-accent) !important; border-color: var(--pt-accent) !important; color: var(--pt-accent-contrast) !important; }");
            sb.AppendLine(".btn-outline-info, .btn-outline-primary, .page-link, .footer-modern .footer-brand, .btn-logout { color: var(--pt-accent-dark) !important; border-color: var(--pt-accent) !important; }");
            sb.AppendLine(".form-control:focus, .form-select:focus, .btn:focus, .btn:active:focus, .form-check-input:focus { border-color: var(--pt-accent) !important; box-shadow: 0 0 0 4px var(--pt-accent-soft) !important; }");
            sb.AppendLine(".pt-swal-confirm { background: linear-gradient(135deg, var(--pt-accent), var(--pt-accent-dark)) !important; color: var(--pt-accent-contrast) !important; }");
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
