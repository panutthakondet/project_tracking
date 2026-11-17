# Colour-vision assistance

Open **Settings → Appearance → การมองเห็นสี → โหมดช่วยแยกสี**, then save. Each user's preference is stored in `user_theme_preferences.color_blind_mode_enabled`; existing accounts default to off. Reset Appearance also turns it off. The idempotent startup schema helper adds the column after the theme preference table is prepared.

The mode reduces red/green dependence using blue for completed/success and a darker orange for danger, with other differentiated colours. It preserves the selected background/theme, font scale, images, uploaded documents, underlying counts and status meanings. Bootstrap status badges gain symbols; owner-chart legend shapes also differ. Existing status names, totals and chart tooltips remain available.

Coverage: shared theme status tokens, common Bootstrap status badges/alerts, owner overview, project/Issues/Support SVG and Three.js donuts, Assigns SVG donut, annual task SVG chart, and attendance charts that already consume shared tokens. Bespoke colours outside these shared tokens/selectors are not automatically transformed. This is a selectable palette aid, not a simulation/correction for every type of colour-vision deficiency or a claim of WCAG certification.

The chart helper returns concrete hex colours for both SVG gradient code and Three.js; it preserves old palette values when the mode is off. No page-wide image filter is used.

## Verification

- `dotnet run --project tests/Appearance/AppearanceTests.csproj --no-restore`: isolated SQLite checks for default off, save/reload, per-user isolation, existing theme/font preservation, disabling and reset. No production database is accessed.
- Browser regression uses the generated theme CSS and real chart scripts with local fixture data at mobile/desktop widths, in standard and enabled modes. It checks WebGL colours, SVG, symbols, overflow and JavaScript errors. A deuteranopia emulation is used as a visual check; it does not substitute for feedback from people with colour-vision differences.
- Reference: [W3C: Use of Color](https://www.w3.org/WAI/WCAG21/Understanding/use-of-color.html). Continue to provide labels/other cues alongside colour in future status widgets.
