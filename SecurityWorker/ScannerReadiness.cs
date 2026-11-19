using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectTracking.SecurityWorker;

public static class ScannerReadiness
{
    public static async Task MobSfAsync(HttpClient client, CancellationToken ct)
    {
        // API-only deployments can return 500 from the login page while their API is healthy.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync("api/v1/scans", timeout.Token);
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
        if (report.ValueKind != JsonValueKind.Object || report.TryGetProperty("error", out _))
            throw new InvalidOperationException("MobSF readiness API returned an invalid response");
    }
}
