using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProjectTracking.Services;

public static class EmployeeNoteRichText
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static readonly int[] FontSizes = { 12, 14, 16, 18, 20, 24, 28, 32, 36, 48 };
    public sealed class Run
    {
        public string Text { get; set; } = "";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public string? Color { get; set; }
        public string? Highlight { get; set; }
        public int? FontSize { get; set; }
        [JsonIgnore]
        public string Css => $"font-weight:{(Bold ? 700 : 400)};font-style:{(Italic ? "italic" : "normal")};text-decoration:{(Underline ? "underline" : "none")};"
            + (Color != null ? $"color:{Color};" : "")
            + (Highlight != null ? $"background-color:{Highlight};" : "")
            + (FontSize.HasValue ? $"font-size:{FontSize.Value}px;" : "");
    }
    public sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<Run> Runs { get; set; } = new();
    }

    private static string? Color(string? value) => value != null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value.ToLowerInvariant() : null;

    public static Document Parse(string content)
    {
        var doc = JsonSerializer.Deserialize<Document>(content, Options);
        if (doc == null || doc.Version != 1 || doc.Runs == null || doc.Runs.Any(x => x == null))
            throw new InvalidDataException("รูปแบบบันทึกไม่ถูกต้อง");
        foreach (var run in doc.Runs)
        {
            run.Text ??= "";
            run.Color = Color(run.Color);
            run.Highlight = Color(run.Highlight);
            if (!FontSizes.Contains(run.FontSize ?? 0)) run.FontSize = null;
        }
        return doc;
    }

    public static Document Read(string content, bool isRichText)
    {
        if (isRichText)
        {
            try { return Parse(content); }
            catch (JsonException) { }
            catch (InvalidDataException) { }
        }
        return new Document { Runs = new() { new Run { Text = content } } };
    }

    public static string Normalize(string content) => JsonSerializer.Serialize(Parse(content), Options);
    public static string Serialize(Document document) => JsonSerializer.Serialize(document, Options);
    public static string PlainText(string content, bool isRichText) => string.Concat(Read(content, isRichText).Runs.Select(x => x.Text));
}
