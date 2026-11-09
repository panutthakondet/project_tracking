using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTracking.Services;

// Store a small formatting model, never user-supplied HTML.
public static class EmployeeNoteRichText
{
    public sealed record Run(string Text, bool Bold = false, bool Italic = false, bool Underline = false, string? Color = null, string? Highlight = null, int Size = 16);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private static bool IsColor(string? value) => value != null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
    public static string FromPlainText(string content) => JsonSerializer.Serialize(new { version = 2, runs = new[] { new Run(content) } }, Options);
    public static string? Normalize(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.GetProperty("version").GetInt32() != 2) return null;
            var runs = JsonSerializer.Deserialize<List<Run>>(doc.RootElement.GetProperty("runs").GetRawText(), Options);
            if (runs == null || runs.Any(r => r == null || r.Text == null)) return null;
            return JsonSerializer.Serialize(new { version = 2, runs = runs.Select(r => r with { Color = IsColor(r.Color) ? r.Color : null, Highlight = IsColor(r.Highlight) ? r.Highlight : null, Size = Math.Clamp(r.Size, 12, 48) }) }, Options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }
    public static string PlainText(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var version = doc.RootElement.GetProperty("version").GetInt32();
            if (version != 1 && version != 2) return content;
            return string.Concat(doc.RootElement.GetProperty("runs").EnumerateArray().Select(r => r.GetProperty("text").GetString() ?? ""));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return content; }
    }
    public static string ToHtml(string content, bool rich)
    {
        var normalized = rich ? Normalize(content) : null;
        if (normalized == null) return WebUtility.HtmlEncode(rich ? PlainText(content) : content).Replace("\n", "<br>");
        using var doc = JsonDocument.Parse(normalized);
        var runs = JsonSerializer.Deserialize<List<Run>>(doc.RootElement.GetProperty("runs").GetRawText(), Options)!;
        return string.Concat(runs.Select(r => $"<span style=\"font-weight:{(r.Bold ? 700 : 400)} !important;font-style:{(r.Italic ? "italic" : "normal")} !important;text-decoration:{(r.Underline ? "underline" : "none")} !important;font-size:{r.Size}px !important;{(r.Color == null ? "" : $"color:{r.Color} !important;-webkit-text-fill-color:{r.Color} !important;")}{(r.Highlight == null ? "" : $"background-color:{r.Highlight} !important;")}\">{WebUtility.HtmlEncode(r.Text).Replace("\n", "<br>")}</span>"));
    }
}
