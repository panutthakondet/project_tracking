using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectTracking.Services;

public sealed record SyncObject(string Name, Dictionary<string, string> Columns);
public sealed record SyncPair(int Maria, int Oracle, string? SumColumns);
public sealed record SyncMetric(string Table, string Metric, decimal? Maria, decimal? Oracle, string Status);
public sealed record SyncDraft(SyncObject[] Maria, SyncObject[] Oracle);
public sealed record SyncResult(SyncPair[] Pairs, SyncMetric[] Metrics);

public static class SyncComparison
{
    private const string Identifier = "[A-Za-z_][A-Za-z0-9_]*";

    public static (int Total, int Matching, int Different) SummarizeTables(IEnumerable<SyncMetric> metrics)
    {
        var tables = metrics
            .Where(metric => !metric.Metric.StartsWith("NON_NULL:", StringComparison.OrdinalIgnoreCase))
            .GroupBy(metric => metric.Table, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matching = tables.Count(table => table.All(metric => metric.Status == "ตรงกัน"));
        return (tables.Count, matching, tables.Count - matching);
    }

    public static SyncObject[] ReadStructure(string input, bool oracle)
    {
        var pattern = oracle
            ? $"""(?im)^\s*CREATE[^\r\n]*?\bVIEW\s+"({Identifier})"\."({Identifier})"\s*\(([^)]*)\)\s+AS\b"""
            : $"""(?im)^\s*CREATE TABLE(?: IF NOT EXISTS)?\s+`({Identifier})`\s*\(([\s\S]*?)^\s*\)""";
        var objects = Regex.Matches(input, pattern, RegexOptions.None, TimeSpan.FromSeconds(2))
            .Select(match => oracle
                ? new SyncObject(match.Groups[1].Value + "." + match.Groups[2].Value,
                    Regex.Matches(match.Groups[3].Value, $"\"({Identifier})\"")
                        .ToDictionary(column => column.Groups[1].Value, _ => "", StringComparer.OrdinalIgnoreCase))
                : new SyncObject(match.Groups[1].Value,
                    Regex.Matches(match.Groups[2].Value, $"""(?im)^\s*`({Identifier})`\s+([a-z]+(?:\([^)]*\))?)""")
                        .ToDictionary(column => column.Groups[1].Value, column => column.Groups[2].Value, StringComparer.OrdinalIgnoreCase)))
            .Where(item => item.Columns.Count > 0).ToArray();
        if (objects.Length is 0 or > 200 || objects.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != objects.Length)
            throw new FormatException("อ่านโครงสร้างไม่ได้ พบชื่อซ้ำ หรือมีเกิน 200 รายการ");
        return objects;
    }

    public static int Suggest(SyncObject item, SyncObject[] oracle) =>
        Array.FindIndex(oracle, candidate => string.Equals(
            candidate.Name.Split('.')[^1], "VM_" + (item.Name.StartsWith("sm_", StringComparison.OrdinalIgnoreCase) ? item.Name[3..] : item.Name), StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name.Split('.')[^1], item.Name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> Sums(SyncObject maria, SyncObject oracle) =>
        maria.Columns.Keys.Where(column => oracle.Columns.Keys.Contains(column, StringComparer.OrdinalIgnoreCase)
            && Regex.IsMatch(maria.Columns[column], @"^(decimal|numeric|double|float|real)(\(|$)", RegexOptions.IgnoreCase));

    public static (string Maria, string Oracle) MakeSql(SyncDraft draft, SyncPair[] pairs)
    {
        var mariaSql = new List<string>();
        var oracleSql = new List<string>();
        if (pairs.Length == 0 || pairs.Select(p => p.Maria).Distinct().Count() != pairs.Length
            || pairs.Select(p => p.Oracle).Distinct().Count() != pairs.Length)
            throw new FormatException("เลือกคู่ตารางอย่างน้อยหนึ่งคู่ และใช้แต่ละตารางหรือ View เพียงครั้งเดียว");
        foreach (var pair in pairs)
        {
            if ((uint)pair.Maria >= draft.Maria.Length || (uint)pair.Oracle >= draft.Oracle.Length)
                throw new FormatException("คู่ตารางไม่ถูกต้อง");
            var maria = draft.Maria[pair.Maria];
            var oracle = draft.Oracle[pair.Oracle];
            var sums = (pair.SumColumns ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            Add("ROW_COUNT", "COUNT(*)", "COUNT(*)");
            foreach (var column in sums)
            {
                var name = Sums(maria, oracle).FirstOrDefault(value => value.Equals(column, StringComparison.OrdinalIgnoreCase));
                if (name == null) throw new FormatException("เลือก SUM เฉพาะคอลัมน์ตัวเลขที่มีชื่อร่วมกัน");
                var oracleName = oracle.Columns.Keys.First(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
                Add($"SUM:{name}", $"SUM(`{name}`)", $"""SUM("{oracleName}")""");
            }

            void Add(string metric, string mariaExpression, string oracleExpression)
            {
                var projection = $"SELECT '{maria.Name}' AS table_key, '{metric}' AS metric, ";
                mariaSql.Add(projection + $"{mariaExpression} AS metric_value FROM `{maria.Name}`");
                oracleSql.Add(projection + $"{oracleExpression} AS metric_value FROM " +
                    string.Join(".", oracle.Name.Split('.').Select(part => $"\"{part}\"")));
            }
        }

        return (Join(mariaSql), Join(oracleSql));
        static string Join(List<string> statements) => string.Join("\nUNION ALL\n", statements) + "\nORDER BY table_key, metric;\n";
    }

    public static SyncMetric[] Compare(string maria, string oracle, string expectedSql, SyncDraft draft)
    {
        var expected = Regex.Matches(expectedSql, @"^SELECT '([^']+)' AS table_key, '([^']+)' AS metric", RegexOptions.Multiline)
            .Select(match => (match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value.ToUpperInvariant())).ToHashSet();
        var selectedTables = expected.Select(key => key.Item1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludedTables = draft.Maria.Select(table => table.Name)
            .Where(name => !selectedTables.Contains(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = ReadCsv(maria, excludedTables);
        var right = ReadCsv(oracle, excludedTables);
        if (left.Keys.Concat(right.Keys).Any(key => !expected.Contains(key)))
            throw new FormatException("พบตัวชี้วัดที่ไม่อยู่ในชุด SELECT ของรอบนี้");
        return expected.OrderBy(key => key.Item1).ThenBy(key => key.Item2).Select(key =>
        {
            var hasMaria = left.TryGetValue(key, out var a);
            var hasOracle = right.TryGetValue(key, out var b);
            var status = !hasMaria && !hasOracle ? "ขาดทั้งสองฝั่ง" : !hasMaria ? "ขาด MariaDB" : !hasOracle ? "ขาด Oracle" : a == b ? "ตรงกัน" : "ต่างกัน";
            return new SyncMetric(key.Item1, key.Item2, a, b, status);
        }).ToArray();
    }

    private static Dictionary<(string, string), decimal?> ReadCsv(string input, HashSet<string> excludedTables)
    {
        using var parser = new TextFieldParser(new StringReader(input));
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? [];
        var table = Array.FindIndex(headers, h => h.Equals("table_key", StringComparison.OrdinalIgnoreCase));
        var metric = Array.FindIndex(headers, h => h.Equals("metric", StringComparison.OrdinalIgnoreCase));
        var value = Array.FindIndex(headers, h => h.Equals("metric_value", StringComparison.OrdinalIgnoreCase));
        if (headers.Length != 3 || table < 0 || metric < 0 || value < 0)
            throw new FormatException("CSV ต้องมีหัวคอลัมน์ table_key, metric, metric_value");
        var result = new Dictionary<(string, string), decimal?>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields() ?? [];
            if (row.Length != 3) throw new FormatException("จำนวนคอลัมน์ CSV ไม่ถูกต้อง");
            if (excludedTables.Contains(row[table])) continue;
            // Older SQL exports also include a count of non-null values for each SUM.
            if (row[metric].StartsWith("NON_NULL:", StringComparison.OrdinalIgnoreCase)) continue;
            decimal? number = null;
            if (row[value].Length > 0 && !row[value].Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                if (!decimal.TryParse(row[value], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    throw new FormatException("metric_value ต้องเป็นตัวเลขที่ใช้จุดทศนิยม หรือ NULL");
                number = parsed;
            }
            if (!result.TryAdd((row[table].ToUpperInvariant(), row[metric].ToUpperInvariant()), number))
                throw new FormatException("พบตัวชี้วัดซ้ำใน CSV");
        }
        if (result.Count == 0) throw new FormatException("CSV ไม่มีข้อมูล");
        return result;
    }
}
