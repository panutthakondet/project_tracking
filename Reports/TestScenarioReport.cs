namespace ProjectTracking.Reports;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
            .GroupBy(x => x.group_id ?? 0)
            .Select((group, index) => new
            {
                GroupIndex = index + 1,
                GroupKey = group.Key,
                GroupName = group.FirstOrDefault()?.GroupName ?? $"Group {group.Key}",
                Items = group.ToList(),
                Total = group.Count(),
                GroupSectionId = $"group-{index + 1}"
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

                    foreach (var group in grouped)
                    {
                        var groupName = group.GroupName;
                        var count = group.Total;

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"{group.GroupIndex}. {groupName} (Total: {count})")
                                .FontSize(16)
                                .Bold();

                            row.ConstantItem(50).AlignRight().Text(text =>
                            {
                                text.DefaultTextStyle(x => x.FontSize(16));
                                text.BeginPageNumberOfSection(group.GroupSectionId);
                            });
                        });

                        int subIndex = 1;
                        foreach (var item in group.Items)
                        {
                            var itemSectionId = $"scenario-{item.scenario_id}";

                            col.Item().PaddingLeft(20).Row(row =>
                            {
                                row.RelativeItem().Text($"{group.GroupIndex}.{subIndex} {item.scenario_code} : {item.title}")
                                    .FontSize(14)
                                    .FontColor(Colors.Grey.Darken1);

                                row.ConstantItem(50).AlignRight().Text(text =>
                                {
                                    text.DefaultTextStyle(x => x.FontSize(14).FontColor(Colors.Grey.Darken1));
                                    text.BeginPageNumberOfSection(itemSectionId);
                                });
                            });

                            subIndex++;
                        }

                    }
                });
            });

            // ================= DETAIL =================
            var grouped = groupedData;

            foreach (var group in grouped)
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
                        // 🔥 GROUP HEADER
                        var groupName = group.GroupName;
                        var total = group.Total;

                        col.Item().Section(group.GroupSectionId).Text($"{group.GroupIndex}. {groupName} (Total: {total})")
                            .FontSize(18)
                            .Bold()
                            .FontColor(Colors.Black);

                        int sectionIndex = 1;
                        foreach (var item in group.Items)
                        {
                            col.Item().PaddingTop(10).Column(inner =>
                            {
                                inner.Item().Section($"scenario-{item.scenario_id}").Text($"{group.GroupIndex}.{sectionIndex} {item.scenario_code} : {item.title}")
                                    .Bold().FontSize(16);

                                inner.Item().PaddingTop(4).Text(text =>
                                {
                                    text.Span("Status: ").Bold();
                                    var status = item.status ?? "-";
                                    if (status == "FAILED")
                                        text.Span(status).FontColor(Colors.Red.Medium).Bold();
                                    else
                                        text.Span(status);
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
                                                var fullPath = Path.Combine(webRootPath, relative);

                                                e.Column(c =>
                                                {
                                                    // log path
                                                    File.AppendAllText(
                                                        Path.Combine(webRootPath, "error_log.txt"),
                                                        $"CHECK: {fullPath} {DateTime.Now}\n"
                                                    );

                                                    if (File.Exists(fullPath))
                                                    {
                                                        try
                                                        {
                                                            c.Item()
                                                              .AlignCenter()
                                                              .AlignMiddle()
                                                              .Height(140)
                                                              .Element(imgContainer =>
                                                              {
                                                                  using (var stream = File.OpenRead(fullPath))
                                                                  {
                                                                      imgContainer
                                                                          .AlignCenter()
                                                                          .AlignMiddle()
                                                                          .MaxHeight(120)
                                                                          .MaxWidth(180)
                                                                          .Image(stream);
                                                                  }
                                                              });
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            File.AppendAllText(
                                                                Path.Combine(webRootPath, "error_log.txt"),
                                                                $"ERROR IMAGE: {fullPath}\n{ex}\n"
                                                            );

                                                            c.Item().AlignCenter().Text("Image error").FontSize(8).FontColor(Colors.Red.Medium);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        File.AppendAllText(
                                                            Path.Combine(webRootPath, "error_log.txt"),
                                                            $"NOT FOUND: {fullPath}\n"
                                                        );

                                                        c.Item().AlignCenter().Text("Image not found").FontSize(8).FontColor(Colors.Red.Medium);
                                                    }

                                                    // caption
                                                    c.Item().PaddingTop(3).AlignCenter().Text(img.FileName ?? "-")
                                                        .FontSize(8)
                                                        .FontColor(Colors.Grey.Darken1);
                                                });
                                            });
                                        }
                                    });
                                }
                            });

                            col.Item().PaddingBottom(10).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                            sectionIndex++;
                        }
                    });
                });
            }

        }).GeneratePdf();
    }
}