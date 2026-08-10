using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;

namespace ProjectTracking.Controllers;

internal static class ReportDepartmentSupport
{
    internal static async Task<int?> LoadAsync(Controller controller, AppDbContext context, int? departmentId)
    {
        var departments = await context.ProjectDepartments
            .AsNoTracking()
            .Where(row => row.IsActive)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.DepartmentName)
            .ToListAsync();

        if (departmentId.HasValue && departments.All(row => row.DepartmentId != departmentId.Value))
            departmentId = null;

        controller.ViewBag.ProjectDepartments = departments;
        controller.ViewBag.SelectedDepartmentId = departmentId;
        return departmentId;
    }
}
