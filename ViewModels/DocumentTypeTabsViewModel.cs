namespace ProjectTracking.ViewModels;

public class DocumentTypeTabsViewModel
{
    public record Tab(string Code, string Name, int Count);
    public List<Tab> Tabs { get; set; } = new();
    public string SelectedType { get; set; } = "";
    public Dictionary<string, string> RouteValues { get; set; } = new();

    public static string Normalize(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static DocumentTypeTabsViewModel Create(IEnumerable<(string Code, string Name)> types,
        IEnumerable<string?> documentCodes, string? selectedType, Dictionary<string, string> routeValues)
    {
        var codes = documentCodes.Select(Normalize).ToList();
        var counts = codes.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        var tabs = types.Select(x => new Tab(Normalize(x.Code), x.Name, counts.GetValueOrDefault(Normalize(x.Code))))
            .Where(x => x.Code.Length > 0).DistinctBy(x => x.Code).ToList();
        // เอกสารเก่าของประเภทที่ปิดใช้งานแล้วต้องยังเปิดดูได้
        var known = tabs.Select(x => x.Code).ToHashSet();
        tabs.AddRange(counts.Where(x => x.Key.Length > 0 && !known.Contains(x.Key))
            .OrderBy(x => x.Key).Select(x => new Tab(x.Key, x.Key, x.Value)));
        var selected = Normalize(selectedType);
        return new DocumentTypeTabsViewModel
        {
            Tabs = new[] { new Tab("", "ทั้งหมด", codes.Count) }.Concat(tabs).ToList(),
            SelectedType = tabs.Any(x => x.Code == selected) ? selected : "",
            RouteValues = routeValues
        };
    }
}
