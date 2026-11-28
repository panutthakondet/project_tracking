using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using ProjectTracking.Models;

namespace ProjectTracking.Services.Security;

internal static class SecurityFindingGrouping
{
    public static readonly Expression<Func<SecurityScanFinding, SecurityScanFinding>> Summary =
        item => new SecurityScanFinding
        {
            Id = item.Id,
            ScanId = item.ScanId,
            FindingId = item.FindingId,
            RuleId = item.RuleId,
            Title = item.Title,
            Severity = item.Severity,
            Cwe = item.Cwe,
            Location = item.Location
        };

    public static (long ScanId, string RuleId, string Title, string Severity, string? Cwe, string Parameter)
        Key(SecurityScanFinding item) =>
        (item.ScanId, item.RuleId, item.Title, item.Severity, item.Cwe, Parameter(Locations(item.Location).FirstOrDefault() ?? ""));

    public static (string RuleId, string Title, string Severity, string? Cwe, string Parameter)
        Key(ScanObservation item) =>
        (item.RuleId, item.Title, item.Severity, item.Cwe, Parameter(item.Location));

    public static string Fingerprint(string workflow, string engine, string environment, string section, ScanObservation item) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { "case/v1", workflow, engine, environment, section, item.RuleId, item.Title, item.Severity, item.Cwe, Parameter(item.Location) })));

    public static string[] Locations(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string Parameter(string location)
    {
        var start = location.LastIndexOf(" [param: ", StringComparison.Ordinal);
        return start < 0 ? "" : location[start..];
    }
}
