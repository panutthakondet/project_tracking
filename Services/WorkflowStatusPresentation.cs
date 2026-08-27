using ProjectTracking.Models;

namespace ProjectTracking.Services
{
    /// <summary>
    /// Presentation helpers for workflow statuses. Status names and ordering
    /// always come from the status master tables; only visual palettes are
    /// assigned here so newly-added statuses still render consistently.
    /// </summary>
    public static class WorkflowStatusPresentation
    {
        private static readonly string[] TonePalette =
        {
            "blue", "green", "orange", "purple", "cyan", "pink", "lime", "muted"
        };

        private static readonly string[] ColorPalette =
        {
            "#2f8ee8", "#19c979", "#ffb444", "#8b5cf6", "#06b6d4", "#ec4899", "#84cc16", "#94a3b8"
        };

        public static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        public static string Description(
            IEnumerable<StatusDefinitionOption> definitions,
            string? codeOrDescription,
            string fallback = "-")
        {
            var value = (codeOrDescription ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var definition = definitions.FirstOrDefault(x =>
                string.Equals(x.StatusCode, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.StatusDesc, value, StringComparison.OrdinalIgnoreCase));
            return definition?.StatusDesc ?? value;
        }

        public static int SortOrder(IEnumerable<StatusDefinitionOption> definitions, string? codeOrDescription)
        {
            var value = (codeOrDescription ?? string.Empty).Trim();
            var definition = definitions.FirstOrDefault(x =>
                string.Equals(x.StatusCode, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.StatusDesc, value, StringComparison.OrdinalIgnoreCase));
            return definition?.SortOrder ?? int.MaxValue;
        }

        public static string Tone(StatusDefinitionOption definition, int index)
        {
            var code = Normalize(definition.StatusCode);
            if (code is "DONE" or "COMPLETED" or "SUBMITTED") return "green";
            if (code is "IN_PROGRESS" or "WORKING") return "blue";
            if (code.Contains("REJECT", StringComparison.Ordinal) || code.Contains("CANCEL", StringComparison.Ordinal)) return "pink";
            return TonePalette[Math.Abs(index) % TonePalette.Length];
        }

        public static string Color(StatusDefinitionOption definition, int index)
        {
            var code = Normalize(definition.StatusCode);
            if (code is "DONE" or "COMPLETED" or "SUBMITTED") return "#19c979";
            if (code is "IN_PROGRESS" or "WORKING") return "#2f8ee8";
            if (code.Contains("REJECT", StringComparison.Ordinal) || code.Contains("CANCEL", StringComparison.Ordinal)) return "#ef4444";
            return ColorPalette[Math.Abs(index) % ColorPalette.Length];
        }
    }
}
