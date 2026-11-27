using ProjectTracking.Models;
using ProjectTracking.ViewModels.Security;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProjectTracking.Services.Security;

public static class SecurityScanPdf
{
    private static readonly (string Level, string Color)[] SeverityColors =
    [
        ("Critical", "#a81939"), ("High", "#c34223"), ("Medium", "#916000"),
        ("Low", "#2367a0"), ("Info", "#58647a")
    ];

    public static byte[] Create(ProjectSecurityReport report, SecuritySystem system, SecurityVersion version, string fontDirectory)
    {
        var fontFamily = RegisterFonts(fontDirectory);
        var findings = report.Groups.SelectMany(group => group.Findings).ToList();
        var statuses = findings.ToLookup(item => item.Status);
        var awaitingAction = findings.Count(item => item.Severity is "Critical" or "High" &&
            item.Status is "Open" or "InProgress" or "FixedPendingVerification");

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginLeft(18, Unit.Millimetre);
            page.MarginRight(16, Unit.Millimetre);
            page.MarginTop(14, Unit.Millimetre);
            page.MarginBottom(12, Unit.Millimetre);
            page.DefaultTextStyle(style => style.FontFamily(fontFamily).FontSize(14).FontColor("#111111"));
            page.Content().Column(column =>
            {
                column.Item().AlignCenter().Text("ผลการทดสอบความปลอดภัย").FontSize(18).Bold();
                column.Item().AlignCenter().Text(report.ProjectName);
                column.Item().AlignCenter().Text($"{system.Name} · {version.VersionName} / {version.Environment}");

                Heading(column, "สรุปผลรอบสแกน");
                column.Item().Text($"พบ {findings.Count} รายการ · ความเสี่ยง Critical หรือ High ที่ยังต้องแก้ไขหรือตรวจยืนยัน {awaitingAction} รายการ");
                foreach (var status in SecurityValues.ReviewStatuses.Union(statuses.Select(group => group.Key)))
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text(SecurityValues.StatusLabel(status));
                        row.ConstantItem(38).AlignRight().Text(statuses[status].Count().ToString());
                    });
                }

                Heading(column, "จำนวนประเด็นตามระดับความเสี่ยง");
                Chart(column, SeverityColors.Select(level =>
                    (level.Level, level.Color, findings.Count(item => item.Severity == level.Level))).ToArray());

                foreach (var group in report.Groups)
                {
                    Heading(column, "เปรียบเทียบกับรอบสแกนก่อนหน้า");
                    if (group.PreviousScanId is long previousId)
                    {
                        column.Item().Text($"เทียบกับรอบ #{previousId}");
                        Chart(column,
                        [
                            ("พบใหม่", "#2367a0", group.Findings.Count(item => item.IsNew == true)),
                            ("ยังพบอยู่", "#2367a0", group.Findings.Count(item => item.IsNew == false)),
                            ("ไม่พบซ้ำ", "#2367a0", group.NotSeenAgain)
                        ]);
                    }
                    else
                    {
                        column.Item().Text("ยังไม่มีคู่รอบที่สแกนเสร็จและมีขอบเขตเดียวกัน");
                    }

                    Heading(column, $"{group.Category} · {group.Findings.Count} รายการ");
                    if (group.Run is { } run)
                    {
                        column.Item().Text($"รอบ #{run.Scan.Id} · {SecurityValues.StatusLabel(run.Scan.Status)} · {run.Scan.Engine} / {run.Scan.Mode}");
                        column.Item().Text($"เวลาสิ้นสุด (เวลาไทย) {SecurityDisplayDate.Utc(run.Scan.FinishedAt)}");
                        Detail(column, "ขอบเขตการตรวจ", run.Scan.Coverage);
                        if (run.Scan.Status != "Completed")
                            column.Item().Text("ผลยังไม่ครบ สรุปได้เฉพาะส่วนที่สแกนเสร็จแล้ว");
                        foreach (var scan in run.Scans.Where(_ => run.Parts.Count > 0))
                            column.Item().Text($"{SecurityValues.SectionLabel(run.Section(scan.Id))} · {SecurityValues.StatusLabel(scan.Status)}");
                    }

                    if (group.Findings.Count == 0)
                        column.Item().Text("ไม่พบประเด็นในผลที่มี");

                    foreach (var findingGroup in group.Findings.GroupBy(item => item.IsNew).OrderByDescending(items => items.Key == true))
                    {
                        var title = findingGroup.Key switch
                        {
                            true => "รายการที่พบใหม่",
                            false => "รายการที่ยังพบอยู่",
                            null => "รายการจากรอบนี้"
                        };
                        Heading(column, $"{title} · {findingGroup.Count()} รายการ");
                        var number = 0;
                        foreach (var item in findingGroup)
                        {
                            var section = string.IsNullOrWhiteSpace(item.Section) ? "" : $" · {item.Section}";
                            column.Item().PaddingTop(4).Text(line =>
                            {
                                line.Span($"{++number}. {item.Severity} · {item.Title}").Bold();
                                line.Span($" · {SecurityValues.StatusLabel(item.Status)}{section}");
                            });
                            Detail(column, "ตำแหน่งที่พบ", item.Location);
                            Detail(column, "CWE", item.Cwe);
                            Detail(column, "รายละเอียด", item.Description);
                            Detail(column, "หลักฐาน", item.Evidence);
                            Detail(column, "แนวทางแก้ไข", item.Remediation);
                            Detail(column, "อ้างอิง", item.References);
                        }
                    }
                }
            });
            page.Footer().AlignRight().Text(line =>
            {
                line.CurrentPageNumber();
                line.Span(" / ");
                line.TotalPages();
            });
        })).GeneratePdf();
    }

    private static string RegisterFonts(string directory)
    {
        const string family = "TH Sarabun New";
        var regular = Path.Combine(directory, "THSarabunNew.ttf");
        if (!File.Exists(regular)) return "Lato";

        foreach (var path in new[] { regular, Path.Combine(directory, "THSarabunNew Bold.ttf") }.Where(File.Exists))
        {
            using var stream = File.OpenRead(path);
            QuestPDF.Drawing.FontManager.RegisterFontWithCustomName(family, stream);
        }
        return family;
    }

    private static void Heading(ColumnDescriptor column, string title) =>
        column.Item().PaddingTop(10).PaddingBottom(2).Text(title).Bold();

    private static void Detail(ColumnDescriptor column, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        column.Item().PaddingLeft(12).Text(line =>
        {
            line.Span(label + " ").Bold();
            line.Span(value.Trim());
        });
    }

    private static void Chart(ColumnDescriptor column, IReadOnlyList<(string Label, string Color, int Count)> values)
    {
        var scale = Math.Max(1, values.Max(value => value.Count));
        foreach (var value in values)
        {
            column.Item().Row(row =>
            {
                row.ConstantItem(66).Text(value.Label);
                row.RelativeItem().AlignMiddle().Height(8).Background("#E9ECEF").Row(bar =>
                {
                    if (value.Count > 0) bar.RelativeItem(value.Count).Background(value.Color);
                    if (value.Count < scale) bar.RelativeItem(scale - value.Count);
                });
                row.ConstantItem(38).AlignRight().Text(value.Count.ToString());
            });
        }
    }
}
