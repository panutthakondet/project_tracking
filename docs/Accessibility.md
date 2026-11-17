# Colour-vision assistance

Open **Settings → Appearance → การมองเห็นสี → โหมดช่วยแยกสี**, then save. Each user's preference is stored in `user_theme_preferences.color_blind_mode_enabled`; existing accounts default to off. Reset Appearance also turns it off. The idempotent startup schema helper adds the column after the theme preference table is prepared.

Three selectable palettes are stored in `color_vision_mode`: `red-green` (default for existing enabled accounts), `blue-yellow`, and `monochrome`. Unknown values fall back to red-green. These are assistance palettes, not vision simulations or medical correction.

While enabled, the shared theme resolves a neutral page canvas, high contrast dark menus/profile/dashboard panels, readable text, and palette-specific accents. The shared MVC and standalone layouts apply it across the site: navigation, page backgrounds, forms, tables, buttons, pagination, progress indicators, badges and alerts, plus chart palettes. Stored custom colours and presets are retained; disabling returns to them. Images and uploaded documents are not filtered. Decorative colours in third-party content are not rewritten.

Status badges include symbols as well as labels; chart legends retain labels and shape cues. Colour alone must not communicate status. Monochrome uses different luminance levels; labels/tooltips remain necessary. This is not a claim of WCAG certification.

The chart helper returns concrete hex colours for both SVG gradient code and Three.js; it preserves old palette values when the mode is off. No page-wide image filter is used.

## Verification

- `dotnet run --project tests/Appearance/AppearanceTests.csproj --no-restore`: isolated SQLite checks for default off, save/reload, per-user isolation, existing theme/font preservation, disabling and reset. No production database is accessed.
- Browser regression uses the generated theme CSS and real chart scripts with local fixture data at mobile/desktop widths, in standard and enabled modes. It checks WebGL colours, SVG, symbols, overflow and JavaScript errors. A deuteranopia emulation is used as a visual check; it does not substitute for feedback from people with colour-vision differences.
- Reference: [W3C: Use of Color](https://www.w3.org/WAI/WCAG21/Understanding/use-of-color.html). Continue to provide labels/other cues alongside colour in future status widgets.
