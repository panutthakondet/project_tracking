using System.Text;
using ProjectTracking.Models;

namespace ProjectTracking.Services.Security;

public static class SecurityMarkdownReport
{
    public static string Create(SecurityScan scan, SecurityVersion version, SecuritySystem system,
        string projectName, IReadOnlyCollection<SecurityScanFinding> findings)
    {
        var text = new StringBuilder();
        text.AppendLine($"# รายงานตรวจความปลอดภัย #{scan.Id}").AppendLine();
        void Field(string label, string? value)
        {
            text.AppendLine($"### {label}").AppendLine();
            // Keep scanner-controlled HTML, Markdown and backticks as literal report content.
            var content = value ?? "";
            var longest = 0;
            var run = 0;
            foreach (var c in content) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
            var fence = new string('`', Math.Max(3, longest + 1));
            text.AppendLine(fence + "text").AppendLine(content).AppendLine(fence).AppendLine();
        }
        Field("โครงการ", projectName);
        Field("ระบบ", system.Name);
        Field("เวอร์ชัน / Build / สภาพแวดล้อม", $"{version.VersionName} / {version.BuildNumber} / {version.Environment}");
        Field("เครื่องมือ / โหมด / เวอร์ชันเครื่องมือ", $"{scan.Engine} / {scan.Mode} / {scan.EngineVersion}");
        Field("เป้าหมาย", scan.TargetUrl);
        Field("ขอบเขตการตรวจ", scan.Coverage);
        Field("เริ่ม / สิ้นสุด (UTC)", $"{scan.StartedAt:yyyy-MM-dd HH:mm:ss} / {scan.FinishedAt:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"## สรุปผล — {findings.Count} รายการ").AppendLine();
        foreach (var group in findings.GroupBy(x => x.Severity).OrderByDescending(x => SecurityValues.Rank(x.Key)))
            text.AppendLine($"- {group.Key}: {group.Count()}");
        text.AppendLine();
        var number = 0;
        foreach (var item in findings.OrderByDescending(x => SecurityValues.Rank(x.Severity)).ThenBy(x => x.Title).ThenBy(x => x.Id))
        {
            text.AppendLine($"## รายการที่ {++number}").AppendLine();
            Field("หัวข้อ", item.Title);
            Field("ระดับความรุนแรง / Rule / CWE", $"{item.Severity} / {item.RuleId} / {item.Cwe}");
            Field("ตำแหน่ง", item.Location);
            Field("รายละเอียด", item.Description);
            Field("หลักฐาน", item.Evidence);
            Field("แนวทางแก้ไข", item.Remediation);
            Field("แหล่งอ้างอิง", item.References);
        }
        return text.ToString();
    }
}
