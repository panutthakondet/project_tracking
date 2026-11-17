using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
namespace ProjectTracking.Services;
public static class ColorVisionSchema
{
    public static async Task EnsureAsync(IServiceProvider services)
    {
        using var scope=services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection=db.Database.GetDbConnection();
        var close=connection.State!=System.Data.ConnectionState.Open;
        if(close) await connection.OpenAsync();
        try
        {
            using var command=connection.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='user_theme_preferences' AND COLUMN_NAME='color_blind_mode_enabled';";
            if(Convert.ToInt32(await command.ExecuteScalarAsync())==0)
            {
                command.CommandText="ALTER TABLE `user_theme_preferences` ADD COLUMN `color_blind_mode_enabled` tinyint(1) NOT NULL DEFAULT 0;";
                await command.ExecuteNonQueryAsync();
            }
            command.CommandText="SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='user_theme_preferences' AND COLUMN_NAME='color_vision_mode';";
            if(Convert.ToInt32(await command.ExecuteScalarAsync())==0)
            {
                command.CommandText="ALTER TABLE `user_theme_preferences` ADD COLUMN `color_vision_mode` varchar(24) NOT NULL DEFAULT 'red-green';";
                    await command.ExecuteNonQueryAsync();
            }
        }
        finally { if(close)await connection.CloseAsync(); }
    }
}
