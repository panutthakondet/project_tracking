using System.Net;

namespace ProjectTracking.Services;

public static class ProjectRichText
{
    public const string Prefix = "PT-RICH-1:";
    public static string PlainText(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true
        ? EmployeeNoteRichText.PlainText(value[Prefix.Length..]) : value ?? "";
    public static string ToHtml(string? value) => "<span style=\"white-space:pre-wrap;overflow-wrap:anywhere\">" + (value?.StartsWith(Prefix, StringComparison.Ordinal) == true
        ? EmployeeNoteRichText.ToHtml(value[Prefix.Length..], true)
        : WebUtility.HtmlEncode(value ?? "").Replace("\n", "<br>")) + "</span>";
}
