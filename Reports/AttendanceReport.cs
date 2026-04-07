using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProjectTracking.Reports
{
    public class AttendanceReport
    {
        public static byte[] Generate(List<dynamic> data)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(20);

                    // ===== HEADER =====
                    page.Header()
                        .Column(col =>
                        {
                            col.Item().Text("รายงานการเข้างานพนักงาน")
                                .FontSize(16)
                                .Bold();

                            col.Item().Text($"วันที่พิมพ์: {DateTime.Now.AddYears(543):dd/MM/yyyy}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken1);
                        });

                    // ===== CONTENT =====
                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40);   // ลำดับ
                            columns.RelativeColumn(1.5f); // วันที่
                            columns.RelativeColumn(3);    // ชื่อ
                            columns.RelativeColumn(2);    // ตำแหน่ง
                            columns.RelativeColumn(1);    // เวลาเข้า
                            columns.RelativeColumn(1);    // เวลาออก
                            columns.RelativeColumn(1.2f); // ระยะทาง
                        });

                        // ===== HEADER =====
                        table.Header(header =>
                        {
                            string[] headers = { "ลำดับ", "วันที่", "ชื่อ", "ตำแหน่ง", "เวลาเข้า", "เวลาออก", "ระยะทาง (km)" };

                            foreach (var h in headers)
                            {
                                header.Cell().Element(CellHeader).Text(h);
                            }

                            static IContainer CellHeader(IContainer container)
                            {
                                return container
                                    .PaddingVertical(8)
                                    .PaddingHorizontal(6)
                                    .AlignCenter()
                                    .AlignMiddle()
                                    .DefaultTextStyle(x => x.Bold().FontSize(11));
                            }
                        });

                        int i = 1;

                        foreach (var item in data)
                        {
                            var row = (dynamic)item;

                            string date = ((DateTime)row.WorkDate).AddYears(543).ToString("dd/MM/yyyy");
                            string checkin = row.CheckinTime != null ? ((DateTime)row.CheckinTime).ToString("HH:mm") : "-";
                            string checkout = row.CheckoutTime != null ? ((DateTime)row.CheckoutTime).ToString("HH:mm") : "-";
                            string distance = row.DistanceKm != null ? string.Format("{0:0.00}", row.DistanceKm) : "-";

                            string empName = row.EmpName?.ToString() ?? "-";
                            string position = row.Position?.ToString() ?? "-";

                            table.Cell().Element(CellBody).AlignCenter().Text(i.ToString());
                            table.Cell().Element(CellBody).AlignCenter().Text(date);
                            table.Cell().Element(CellBody).Text(empName);
                            table.Cell().Element(CellBody).AlignCenter().Text(position);
                            table.Cell().Element(CellBody).AlignCenter().Text(checkin);
                            table.Cell().Element(CellBody).AlignCenter().Text(checkout);
                            table.Cell().Element(CellBody).AlignCenter().Text(distance);

                            i++;
                        }

                        static IContainer CellBody(IContainer container)
                        {
                            return container
                                .PaddingVertical(6)
                                .PaddingHorizontal(6)
                                .AlignMiddle()
                                .DefaultTextStyle(x => x.FontSize(10));
                        }
                    });

                    // ===== FOOTER =====
                    page.Footer()
                        .AlignCenter()
                        .Text(txt =>
                        {
                            txt.Span("หน้า ");
                            txt.CurrentPageNumber();
                            txt.Span(" / ");
                            txt.TotalPages();
                        });
                });
            }).GeneratePdf();
        }
    }
}