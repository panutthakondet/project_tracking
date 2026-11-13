namespace ProjectTracking.Services;

// Optional external-channel configuration must not prevent independent web delivery.
internal static class NotificationChannelAvailability
{
    public static async Task<bool> CheckAsync(Func<Task<bool>> check, ILogger logger)
    {
        try { return await check(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "External notification channel configuration unavailable; other channels remain independent");
            return false;
        }
    }
}
