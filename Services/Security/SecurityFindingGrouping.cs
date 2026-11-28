using System.Linq.Expressions;
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
        Key(SecurityScanFinding item)
    {
        var start = item.Location.LastIndexOf(" [param: ", StringComparison.Ordinal);
        return (item.ScanId, item.RuleId, item.Title, item.Severity, item.Cwe,
            start < 0 ? "" : item.Location[start..]);
    }
}
