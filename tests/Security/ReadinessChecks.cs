using System.Net;
using System.Text;
using ProjectTracking.SecurityWorker;

public static class ReadinessChecks
{
    public static async Task Run()
    {
        using var handler = new ReadinessHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://mobsf/") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "fixture-api-key");
        await ScannerReadiness.MobSfAsync(client, default);
        Console.WriteLine("PASS MobSF readiness uses authenticated API even when login/root return 500");

        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
        {
            handler.Status = status;
            try { await ScannerReadiness.MobSfAsync(client, default); }
            catch (HttpRequestException) { continue; }
            throw new Exception("MobSF readiness accepted HTTP " + (int)status);
        }
        handler.Status = HttpStatusCode.OK;
        foreach (var body in new[] { "<html>Login</html>", "[]", "{\"error\":\"database unavailable\"}" })
        {
            handler.Body = body;
            try { await ScannerReadiness.MobSfAsync(client, default); }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { continue; }
            throw new Exception("MobSF readiness accepted an invalid API response");
        }
        Console.WriteLine("PASS MobSF readiness rejects authentication, HTTP, HTML and API error failures");
    }

    private sealed class ReadinessHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "{\"content\":[]}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath != "/api/v1/scans")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            if (!request.Headers.TryGetValues("Authorization", out var values) || values.Single() != "fixture-api-key")
                throw new Exception("Missing MobSF API authentication");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
}
