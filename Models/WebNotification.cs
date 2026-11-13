using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
namespace ProjectTracking.Models;

[Table("web_notifications"), Index(nameof(UserId), nameof(ReadAt), nameof(Id)), Index(nameof(DedupKey), IsUnique = true)]
public class WebNotification
{
    [Key] public long Id { get; set; }
    public int UserId { get; set; }
    public int EmpId { get; set; }
    [MaxLength(255)] public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    [MaxLength(1000)] public string TargetUrl { get; set; } = "/";
    [MaxLength(64)] public string DedupKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
[Table("web_push_subscriptions"), Index(nameof(EndpointHash), IsUnique = true), Index(nameof(UserId), nameof(Active))]
public class WebPushSubscription
{
    [Key] public long Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(64)] public string EndpointHash { get; set; } = "";
    [MaxLength(64)] public string SessionHash { get; set; } = "";
    public string ProtectedSubscription { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}
[Table("web_push_deliveries"), Index(nameof(NotificationId), nameof(SubscriptionId), IsUnique = true), Index(nameof(Done), nameof(NextAttemptAt))]
public class WebPushDelivery
{
    [Key] public long Id { get; set; }
    public long NotificationId { get; set; }
    public long SubscriptionId { get; set; }
    public bool Done { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
}
[Table("web_push_keys")]
public class WebPushKey
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public int Id { get; set; } = 1;
    [MaxLength(255)] public string PublicKey { get; set; } = "";
    public string ProtectedPrivateKey { get; set; } = "";
}
