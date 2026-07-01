namespace ProjectTracking.Helpers
{
    public static class TestScenarioDisplay
    {
        public static string NormalizeStatus(string? status)
        {
            return (status ?? "").Trim().ToUpperInvariant() switch
            {
                "PASSED" => "PASSED",
                "FAILED" => "FAILED",
                "READY" => "READY",
                "DRAFT" => "READY",
                "" => "READY",
                var value => value
            };
        }

        public static string StatusText(string? status)
        {
            return NormalizeStatus(status) switch
            {
                "READY" => "พร้อมทดสอบ",
                "PASSED" => "ผ่าน",
                "FAILED" => "ไม่ผ่าน",
                _ => status ?? "-"
            };
        }
    }
}
