using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
namespace ProjectTracking.Services;
public static class WebNotificationSchema
{
    public static async Task EnsureAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        foreach (var sql in Statements) await db.Database.ExecuteSqlRawAsync(sql);
    }
    public static readonly string[] Statements = [
        """
        CREATE TABLE IF NOT EXISTS web_notifications (
          Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, UserId int NOT NULL, EmpId int NOT NULL,
          Title varchar(255) NOT NULL, Message longtext NOT NULL, TargetUrl varchar(1000) NOT NULL,
          DedupKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
          CreatedAt datetime(6) NOT NULL, ReadAt datetime(6) NULL,
          UNIQUE KEY ux_web_notification_dedup (DedupKey), KEY ix_web_notification_user (UserId,ReadAt,Id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE IF NOT EXISTS web_push_subscriptions (
          Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, UserId int NOT NULL,
          EndpointHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
          SessionHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
          ProtectedSubscription longtext NOT NULL, Active tinyint(1) NOT NULL DEFAULT 1, UpdatedAt datetime(6) NOT NULL,
          UNIQUE KEY ux_web_push_endpoint (EndpointHash), KEY ix_web_push_user (UserId,Active), KEY ix_web_push_session (SessionHash)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE IF NOT EXISTS web_push_deliveries (
          Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, NotificationId bigint NOT NULL, SubscriptionId bigint NOT NULL,
          Done tinyint(1) NOT NULL DEFAULT 0, Attempts int NOT NULL DEFAULT 0, NextAttemptAt datetime(6) NOT NULL,
          UNIQUE KEY ux_web_push_delivery (NotificationId,SubscriptionId), KEY ix_web_push_queue (Done,NextAttemptAt)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE IF NOT EXISTS web_push_keys (
          Id int NOT NULL PRIMARY KEY, PublicKey varchar(255) NOT NULL, ProtectedPrivateKey longtext NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """
    ];
}
