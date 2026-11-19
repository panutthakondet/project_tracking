using System.Net;

namespace ProjectTracking.SecurityWorker;

public sealed class ScannerRequestException(string stage, HttpStatusCode status)
    : HttpRequestException($"{stage}: HTTP {(int)status}", null, status)
{
    public string Stage { get; } = stage;
}

public static class ScannerHttp
{
    // Stage names come from code, never from response bodies, URLs or credentials.
    public static void RequireSuccess(HttpResponseMessage response, string stage)
    {
        if (response.StatusCode==HttpStatusCode.NotFound && stage=="ProjectTracking artifact download"
            && response.Headers.TryGetValues("X-Security-Error",out var codes) && codes.Contains("ARTIFACT_FILE_MISSING"))
            throw new ScannerRequestException("ProjectTracking artifact file missing on server; restore the original file or upload a new version",response.StatusCode);
        if (!response.IsSuccessStatusCode)
            throw new ScannerRequestException(stage, response.StatusCode);
    }

    public static bool IsTransient(HttpRequestException error)
    {
        if (!error.StatusCode.HasValue) return true;
        return error.StatusCode.Value is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)error.StatusCode.Value >= 500;
    }
}
