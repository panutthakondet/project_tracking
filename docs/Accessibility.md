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

## 2026-11-17 palette review

- Semantic foregrounds are selected by WCAG relative luminance (black/white), rather than assuming white text is readable on every status colour. All 21 palette/foreground pairs meet 4.5:1 in normal rendering.
- Monochrome uses achromatic chart colours with increased brightness on dark chart panels. It is a display aid, not a simulation of an eye condition.
- Every palette retains status labels and adds status symbols. Owner bars use seven different hatch orientations/spacings. Annual series use diagonal hatching versus dots and distinct borders. Three.js donut surfaces remain smooth; adjacent labelled totals, status symbols and keyboard tooltips provide the non-colour cues. Symbol textures are deliberately excluded because extrusion UVs distort them.
- Switching palettes only updates existing material colours and lighting uniforms; it does not rebuild extrusion geometry or add textures.
- Generic first-page headings and contextual action buttons explicitly retain readable text; validation errors have dashed borders and an exclamation mark. Focus uses a white/black dual indicator.

Reproduce numerical checks:

```sh
dotnet run --project tests/Appearance/AppearanceTests.csproj --no-restore --no-launch-profile
python3 tests/Appearance/audit-color-palettes.py
node tests/Appearance/color-refresh.test.cjs
node tests/Appearance/color-switcher.test.cjs
```

The C# harness exports the actual palettes to `/private/tmp/color-vision-palette-audit.json`. The Python audit uses linear-sRGB Machado-based matrices published in [Chromium](https://chromium.googlesource.com/chromium/src/+/64fd31034bd3390bee89ccec624557980816bf4d%5E%21/) for protanopia, deuteranopia and tritanopia; achromatic checking uses relative luminance. The audited semantic text/background minima are 4.61:1 (red-green including protan/deutan simulations), 4.67:1 (blue-yellow including tritan simulation), and 4.69:1 (monochrome). Pairwise status colours are **not** guaranteed to be distinguishable by colour alone: the report lists close pairs and patterns/labels must remain present.

Browser review uses an isolated gallery rendered with the actual generated theme CSS. This checks common controls, status cues and owner hatches without touching the production database. It does not constitute a page-by-page audit of the entire application, a clinical test, or certification. Feedback from users with each form of colour-vision difference is still needed.
