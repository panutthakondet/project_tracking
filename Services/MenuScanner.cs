using System.Reflection;

public class MenuScanner
{
    public static List<(string Key, string Label)> ScanMenus()
    {
        var result = new HashSet<string>();

        var controllers = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.Name.EndsWith("Controller"));

        foreach (var ctrl in controllers)
        {
            // 🔥 scan class-level attributes
            var ctrlAttrs = ctrl.GetCustomAttributes(true);
            foreach (var attr in ctrlAttrs)
            {
                var type = attr.GetType();

                if (type.Name == "RequireMenuAttribute")
                {
                    var prop = type.GetProperty("Key") ?? type.GetProperty("MenuKey");
                    var value = prop?.GetValue(attr)?.ToString();

                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value.Trim());
                }
            }
            var methods = ctrl.GetMethods();

            foreach (var method in methods)
            {
                var attrs = method.GetCustomAttributes(true);

                foreach (var attr in attrs)
                {
                    var type = attr.GetType();

                    if (type.Name == "RequireMenuAttribute")
                    {
                        var prop = type.GetProperty("Key") ?? type.GetProperty("MenuKey");
                        var value = prop?.GetValue(attr)?.ToString();

                        if (!string.IsNullOrWhiteSpace(value))
                            result.Add(value.Trim());
                    }
                }
            }
        }

        return result
            .OrderBy(x => x)
            .Select(x => (x, x))
            .ToList();
    }
}