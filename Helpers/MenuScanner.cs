using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Middleware;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ProjectTracking.Helpers
{
    public static class MenuScanner
    {
        public static List<(string Key, string Label)> ScanMenus()
        {
            var result = new List<(string Key, string Label)>();

            var assembly = Assembly.GetExecutingAssembly();

            var controllers = assembly.GetTypes()
                .Where(t =>
                    (typeof(Controller).IsAssignableFrom(t) || typeof(ControllerBase).IsAssignableFrom(t)) &&
                    t.IsClass &&
                    !t.IsAbstract &&
                    t.Name.EndsWith("Controller")
                )
                .ToList();

            Console.WriteLine($"🧠 Controllers detected = {controllers.Count()}");

            foreach (var ctrl in controllers)
            {
                Console.WriteLine($"➡️ Scanning Controller: {ctrl.Name}");

                // 🔥 scan ที่ระดับ Controller (class)
                var ctrlAttrs = ctrl.GetCustomAttributes(inherit: true)
                    .Where(a => a.GetType().Name.Contains("RequireMenu") 
                             || a is IAuthorizationFilter);
                Console.WriteLine($"   👉 Controller Attr count: {ctrlAttrs.Count()}");

                foreach (var attr in ctrlAttrs)
                {
                    var type = attr.GetType();
                    Console.WriteLine($"🔍 Attr Type: {type.FullName}");

                    var prop = type.GetProperty("Key") ?? type.GetProperty("MenuKey");
                    var value = prop?.GetValue(attr)?.ToString();

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        Console.WriteLine($"✅ Found Menu: {value}");
                        result.Add((value!, value!));
                    }
                }

                var methods = ctrl.GetMethods(BindingFlags.Instance | BindingFlags.Public);

                foreach (var m in methods)
                {
                    Console.WriteLine($"   🔹 Method: {m.Name}");

                    if (m.IsSpecialName) continue;

                    var attrs = m.GetCustomAttributes(inherit: true)
                        .Where(a => a.GetType().Name.Contains("RequireMenu") 
                                 || a is IAuthorizationFilter);
                    Console.WriteLine($"      👉 Attr count: {attrs.Count()}");

                    foreach (var attr in attrs)
                    {
                        var type = attr.GetType();
                        Console.WriteLine($"🔍 Attr Type: {type.FullName}");

                        var prop = type.GetProperty("Key") ?? type.GetProperty("MenuKey");
                        var value = prop?.GetValue(attr)?.ToString();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            Console.WriteLine($"✅ Found Menu: {value}");
                            result.Add((value!, value!));
                        }
                    }
                }
            }

            Console.WriteLine($"🔥 MenuScanner found = {result.Count} (Controllers scanned = {controllers.Count()})");

            return result
                .GroupBy(x => x.Key)
                .Select(g => g.First())
                .ToList();
        }
    }
}