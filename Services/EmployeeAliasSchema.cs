using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;

namespace ProjectTracking.Services;

public static class EmployeeAliasSchema
{
    public static async Task EnsureAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'employee' AND COLUMN_NAME = 'alias';";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0) return;
            command.CommandText = "ALTER TABLE `employee` ADD COLUMN `alias` varchar(80) CHARACTER SET utf8mb4 NULL;";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
