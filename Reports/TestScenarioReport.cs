namespace ProjectTracking.Reports;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectTracking.Helpers;
using ProjectTracking.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using QuestPDF.Drawing;
using System.Globalization;

public class TestScenarioReport
{
    public byte[] Generate(List<TestScenario> data, List<TestScenarioAttachment> attachments, string projectName, string webRootPath)
    {
        var fontPath = Path.Combine(webRootPath, "fonts");

        var regularFont = Path.Combine(fontPath, "THSarabunNew.ttf");
        var boldFont = Path.Combine(fontPath, "THSarabunNew-Bold.ttf");

        if (File.Exists(regularFont))
            FontManager.RegisterFont(File.OpenRead(regularFont));

        if (File.Exists(boldFont))
            FontManager.RegisterFont(File.OpenRead(boldFont));

        var logoPath = Path.Combine(webRootPath, "soat/Logo.png");

        var groupedData = data
            .GroupBy(x => string.IsNullOrWhiteSpace(x.ControlName) ? "ยังไม่กำหนด Control" : x.ControlName)
            .Select((control, controlIndex) => new
            {
                ControlIndex = controlIndex + 1,
                ControlName = control.Key,
                Total = control.Count(),
                ControlSectionId = $"control-{controlIndex + 1}",
                Groups = control
                    .GroupBy(x => new
                    {
                        GroupId = x.group_id ?? 0,
                        GroupName = string.IsNullOrWhiteSpace(x.GroupName) ? "ไม่ระบุ Group" : x.GroupName
                    })
                    .Select((group, groupIndex) => new
                    {
                        GroupIndex = groupIndex + 1,
                        GroupKey = group.Key.GroupId,
                        GroupName = group.Key.GroupName,
                        Items = group.ToList(),
                        Total = group.Count(),
                        GroupSectionId = $"control-{controlIndex + 1}-group-{groupIndex + 1}"
                    })
                    .ToList()
            })
            .ToList();

        return Document.Create(container =>
        {
            // ================= COVER =================
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);

                var bgPath = Path.Combine(webRootPath, "soat/Picture1.png");

                page.Content().Extend().Layers(layers =>
                {
                    // Background
                    layers.Layer().Element(layer =>
                    {
                        if (File.Exists(bgPath))
                            layer.Extend().Image(bgPath).FitArea();
                    });

                    // Foreground content
                    layers.PrimaryLayer().DefaultTextStyle(x => x.FontFamily("TH Sarabun New")).PaddingLeft(60).PaddingRight(60).PaddingBottom(60).Column(col =>
                    {
                        col.Item().PaddingTop(0).AlignRight().Element(e =>
                        {
                            if (File.Exists(logoPath))
                                e.Width(220).Image(logoPath);
                        });

                        col.Item().PaddingTop(100).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("Test Scenario Report")
                                .FontSize(30).Bold();

                            c.Item().PaddingTop(10).AlignRight().Text(projectName)
                                .FontSize(24).Bold();

                            c.Item().PaddingTop(20).AlignRight().Text($"วันที่ {DateTime.Now.ToString("dd MMMM yyyy", new CultureInfo("th-TH"))}").FontSize(18).Bold();
                        });
                    });
                });
            });

            // ================= TOC =================
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);

                page.Content().DefaultTextStyle(x => x.FontFamily("TH Sarabun New")).Column(col =>
                {
                    col.Item().Text("สารบัญ")
                        .FontSize(28)
                        .Bold()
                        .AlignCenter();

                    col.Item().PaddingTop(20);

                    var grouped = groupedData;

                    // numbering comes from groupedData

                    foreach (var control in grouped)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"{control.ControlIndex}. {control.ControlName} (Total: {control.Total})")
                                .FontSize(17)
                                .Bold();

                            row.ConstantItem(50).AlignRight().Text(text =>
                            {
                                text.DefaultTextStyle(x => x.FontSize(17));
                                text.BeginPageNumberOfSection(control.ControlSectionId);
                            });
                        });

                        foreach (var group in control.Groups)
                        {
                            col.Item().PaddingLeft(20).Row(row =>
                            {
                                row.RelativeItem().Text($"{control.ControlIndex}.{group.GroupIndex} {group.GroupName} (Total: {group.Total})")
                                    .FontSize(15)
                                    .Bold();

                                row.ConstantItem(50).AlignRight().Text(text =>
                                {
                                    text.DefaultTextStyle(x => x.FontSize(15));
                                    text.BeginPageNumberOfSection(group.GroupSectionId);
                                });
                            });

                            int scenarioIndex = 1;
                            foreach (var item in group.Items)
                            {
                                var itemSectionId = $"scenario-{item.scenario_id}";

                                col.Item().PaddingLeft(40).Row(row =>
                                {
                                    row.RelativeItem().Text($"{control.ControlIndex}.{group.GroupIndex}.{scenarioIndex} {item.scenario_code} : {item.title}")
                                        .FontSize(13)
                                        .FontColor(Colors.Grey.Darken1);

                                    row.ConstantItem(50).AlignRight().Text(text =>
                                    {
                                        text.DefaultTextStyle(x => x.FontSize(13).FontColor(Colors.Grey.Darken1));
                                        text.BeginPageNumberOfSection(itemSectionId);
                                    });
                                });

                                scenarioIndex++;
                            }
                        }
                    }
                });
            });

            // ================= DETAIL =================
            var grouped = groupedData;

            foreach (var control in grouped)
            {
                foreach (var group in control.Groups)
                {
                    container.Page(page =>
                    {
                    page.Size(PageSizes.A4);
                    page.Margin(30);

                    page.Header().Column(header =>
                    {
                        header.Item().Row(row =>
                        {
                            row.ConstantItem(120).Element(e =>
                            {
                                if (File.Exists(logoPath))
                                    e.Height(60).Width(120).Image(logoPath).FitWidth();
                            });

                           row.RelativeItem().AlignRight().Text(projectName)
                                .FontSize(10)
                                .Bold()
                                .FontColor(Colors.Grey.Medium);
                        });

                        header.Item().PaddingTop(0).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                    });

                    page.Content().DefaultTextStyle(x => x.FontFamily("TH Sarabun New")).PaddingTop(10).Padding(5).Column(col =>
                    {
                        var controlTitle = $"{control.ControlIndex}. {control.ControlName} (Total: {control.Total})";
                        if (group.GroupIndex == 1)
                        {
                            col.Item().Section(control.ControlSectionId)
                                .Background(Colors.Blue.Lighten5)
                                .BorderBottom(1)
                                .BorderColor(Colors.Blue.Lighten2)
                                .Padding(8)
                                .Text(controlTitle)
                                .FontSize(19)
                                .Bold()
                                .FontColor(Colors.Blue.Darken3);
                        }

                        col.Item().PaddingTop(8).Section(group.GroupSectionId)
                            .Text($"{control.ControlIndex}.{group.GroupIndex} {group.GroupName} (Total: {group.Total})")
                            .FontSize(17)
                            .Bold()
                            .FontColor(Colors.Black);

                        int sectionIndex = 1;
                        foreach (var item in group.Items)
                        {
                            col.Item().PaddingTop(10).Column(inner =>
                            {
                                inner.Item().Section($"scenario-{item.scenario_id}").Text($"{control.ControlIndex}.{group.GroupIndex}.{sectionIndex} {item.scenario_code} : {item.title}")
                                    .Bold().FontSize(16);

                                inner.Item().PaddingTop(4).Text(text =>
                                {
                                    text.Span("สถานะ: ").Bold();
                                    var status = (item.status ?? "").Trim().ToUpperInvariant();
                                    var statusText = TestScenarioDisplay.StatusText(item.status);
                                    if (status == "FAILED")
                                        text.Span(statusText).FontColor(Colors.Red.Medium).Bold();
                                    else
                                        text.Span(statusText);
                                });

                                inner.Item().PaddingTop(2).Text(text =>
                                {
                                    text.Span("Priority: ").Bold();
                                    text.Span(item.priority ?? "-");
                                });

                                inner.Item().PaddingTop(2).Text(text =>
                                {
                                    text.Span("Precondition: ").Bold();
                                    text.Span(item.precondition ?? "-");
                                });

                                inner.Item().PaddingTop(2).Text(text =>
                                {
                                    text.Span("Steps: ").Bold();
                                    text.Span(item.steps ?? "-");
                                });

                                inner.Item().PaddingTop(2).Text(text =>
                                {
                                    text.Span("Expected Result: ").Bold();
                                    text.Span(item.expected_result ?? "-");
                                });

                                if (!string.IsNullOrWhiteSpace(item.remark))
                                {
                                    inner.Item().PaddingTop(4).Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten4).Padding(8).Column(note =>
                                    {
                                        note.Item().Text("Remark").Bold().FontColor(Colors.Grey.Darken2);
                                        note.Item().PaddingTop(2).Text(item.remark).FontSize(12);
                                    });
                                }

                                // ================= IMAGES =================
                                var imgs = attachments.Where(a => a.ScenarioId == item.scenario_id).ToList();

                                if (imgs.Any())
                                {
                                    inner.Item().PaddingTop(6).Text("Images").Bold();

                                    inner.Item().PaddingTop(5).Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.RelativeColumn();
                                            columns.RelativeColumn();
                                        });

                                        foreach (var img in imgs.Take(4))
                                        {
                                            table.Cell().Padding(8).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Element(e =>
                                            {
                                                var relative = (img.FilePath ?? string.Empty).TrimStart('/');
                                                relative = relative.Replace("/", Path.DirectorySeparatorChar.ToString());

                                                var fullPath = Path.Combine(webRootPath, relative);

                                                e.Column(c =>
                                                {
                                                    if (File.Exists(fullPath))
                                                    {
                                                        try
                                                        {
                                                            c.Item()
                                                              .Border(1)
                                                              .BorderColor(Colors.Grey.Lighten2)
                                                              .Padding(5)
                                                              .AlignCenter()
                                                              .AlignMiddle()
                                                              .Height(150)
                                                              .Element(imgContainer =>
                                                              {
                                                                  using (var stream = File.OpenRead(fullPath))
                                                                  {
                                                                      imgContainer
                                                                          .AlignCenter()
                                                                          .AlignMiddle()
                                                                          .Image(stream)
                                                                          .FitArea();
                                                                  }
                                                              });
                                                        }
                                                        catch (Exception)
                                                        {
                                                            c.Item().AlignCenter().Text("Image error").FontSize(8).FontColor(Colors.Red.Medium);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        c.Item().AlignCenter().Text("Image not found").FontSize(8).FontColor(Colors.Red.Medium);
                                                    }

                                                    c.Item().PaddingTop(5).AlignCenter().Text(img.FileName ?? "-")
                                                        .FontSize(9)
                                                        .FontColor(Colors.Grey.Darken2);
                                                });
                                            });
                                        }
                                    });
                                }
                            });

                            col.Item().PaddingTop(5);
                            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                            col.Item().PaddingBottom(10);
                            sectionIndex++;
                        }
                    });
                    });
                }
            }

        }).GeneratePdf();
    }
}
