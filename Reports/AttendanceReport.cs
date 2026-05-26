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
                            columns.ConstantColumn(35);   // ลำดับ
                            columns.RelativeColumn(1.3f); // วันที่
                            columns.RelativeColumn(2.8f); // ชื่อ
                            columns.RelativeColumn(2.2f); // ตำแหน่ง
                            columns.RelativeColumn(1.2f); // เวลาเข้า
                            columns.RelativeColumn(1.2f); // เวลาออก
                            columns.RelativeColumn(1.1f); // ระยะทาง
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
                                    .Background(Colors.Blue.Lighten4)
                                    .Border(1)
                                    .BorderColor(Colors.Grey.Lighten1)
                                    .PaddingVertical(8)
                                    .PaddingHorizontal(5)
                                    .AlignCenter()
                                    .AlignMiddle()
                                    .DefaultTextStyle(x => x.Bold().FontSize(10));
                            }
                        });

                        int i = 1;

                        foreach (var item in data)
                        {
                            bool isEvenRow = i % 2 == 0;
                            var row = (dynamic)item;

                            string date = ((DateTime)row.WorkDate).AddYears(543).ToString("dd/MM/yyyy");
                            string checkin = row.CheckinTime != null
                                ? ((DateTime)row.CheckinTime).ToString("HH:mm")
                                : "ไม่เช็คเข้า";

                            string checkout = row.CheckoutTime != null
                                ? ((DateTime)row.CheckoutTime).ToString("HH:mm")
                                : "ไม่เช็คออก";

                            string distance = row.DistanceKm != null
                                ? string.Format("{0:0.00}", row.DistanceKm)
                                : "0.00";

                            string empName = row.EmpName?.ToString() ?? "-";
                            string position = row.Position?.ToString() ?? "-";

                            table.Cell().Element(c => CellBody(c)
                                .Background(isEvenRow
                                    ? Colors.Grey.Lighten5
                                    : Colors.White)).AlignCenter().Text(i.ToString());
                            table.Cell().Element(c => CellBody(c)
                                .Background(isEvenRow
                                    ? Colors.Grey.Lighten5
                                    : Colors.White)).AlignCenter().Text(date);
                            table.Cell().Element(c => CellBody(c)
                                .Background(isEvenRow
                                    ? Colors.Grey.Lighten5
                                    : Colors.White)).Text(empName);
                            table.Cell().Element(c => CellBody(c)
                                .Background(isEvenRow
                                    ? Colors.Grey.Lighten5
                                    : Colors.White)).Text(position);

                            table.Cell().Element(c => CellBody(c)
                                .Background(isEvenRow
                                    ? Colors.Grey.Lighten5
                                    : Colors.White))
                                .AlignCenter()
                                .Text(checkin)
                                .FontColor(checkin == "ไม่เช็คเข้า"
                                    ? Colors.Red.Darken2
                                    : Colors.Black);

                            table.Cell().Element(c => CellBody(c)
                                .Background(isEvenRow
                                    ? Colors.Grey.Lighten5
                                    : Colors.White))
                                .AlignCenter()
                                .Text(checkout)
                                .FontColor(checkout == "ไม่เช็คออก"
                                    ? Colors.Orange.Darken2
                                    : Colors.Black);

                            table.Cell().Element(c => CellBody(c)
                                .Background(isEvenRow
                                    ? Colors.Grey.Lighten5
                                    : Colors.White)).AlignCenter().Text(distance);

                            i++;
                        }

                        static IContainer CellBody(IContainer container)
                        {
                            return container
                                .BorderBottom(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .PaddingVertical(5)
                                .PaddingHorizontal(5)
                                .AlignMiddle()
                                .DefaultTextStyle(x => x.FontSize(9.5f));
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