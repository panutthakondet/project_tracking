using System.Text.Json;

namespace ProjectTracking.Services;

// อ่านข้อมูลจากโน้ตที่เคยใช้ตัวแก้ไขแบบจัดรูปแบบ เพื่อกลับมาแสดงเป็นข้อความธรรมดา
public static class EmployeeNoteRichText
{
    public static string PlainText(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) return content;
            return string.Concat(root.GetProperty("runs").EnumerateArray()
                .Select(run => run.GetProperty("text").GetString() ?? ""));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return content;
        }
    }
}
