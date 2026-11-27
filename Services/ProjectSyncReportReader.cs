using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Services;

public static class ProjectSyncReportReader
{
    public static async Task<ProjectSyncRun?> ReadLatestAsync(AppDbContext db, int projectId, CancellationToken ct)
    {
        ProjectSyncRun? syncRun = null;
        try
        {
            syncRun = await db.Set<ProjectSyncRun>().AsNoTracking()
                .Where(run => run.ProjectId == projectId && run.ComparedAtUtc != null && run.ResultJson != null)
                .OrderByDescending(run => run.ComparedAtUtc).ThenByDescending(run => run.Id)
                .Select(run => new ProjectSyncRun { Id = run.Id, ComparedAtUtc = run.ComparedAtUtc, ResultJson = run.ResultJson })
                .FirstOrDefaultAsync(ct);
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1146)
        {
            // The Sync feature is optional on existing databases.
        }
        return syncRun;
    }
}
