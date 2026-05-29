using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Middleware;

namespace ProjectTracking.Helpers
{
    public static class MenuScanner
    {
        public static List<(string Key, string Label)> ScanMenus()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var controllers = assembly.GetTypes()
                .Where(t =>
                    (typeof(Controller).IsAssignableFrom(t) || typeof(ControllerBase).IsAssignableFrom(t)) &&
                    t.IsClass &&
                    !t.IsAbstract &&
                    t.Name.EndsWith("Controller")
                )
                .ToList();

            return controllers
                .SelectMany(ctrl =>
                    ctrl.GetCustomAttributes<RequireMenuAttribute>(inherit: true)
                        .Concat(ctrl.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                            .Where(m => !m.IsSpecialName)
                            .SelectMany(m => m.GetCustomAttributes<RequireMenuAttribute>(inherit: true))))
                .Select(attr => attr.Key?.Trim())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key)
                .Select(key => (key!, key!))
                .ToList();
        }
    }
}
