using System.Net;
using System.Net.Http.Json;
using ProjectTracking.Services.Security;
namespace ProjectTracking.SecurityWorker;

public sealed class ScanProgressReporter : IDisposable
{
    private ScanProgressUpdate current = new("Preparing");
    private readonly SemaphoreSlim changed = new(1);
    public void Update(ScanProgressUpdate progress)
    {
        var previous = Interlocked.Exchange(ref current, progress);
        if (previous != progress) changed.Release();
    }
    public async Task RunAsync(HttpClient api,SecurityJob job,CancellationTokenSource work,CancellationToken stop)
    {
        try {
            while (!stop.IsCancellationRequested) {
                await changed.WaitAsync(TimeSpan.FromSeconds(10),stop);
                while (changed.Wait(0)) { }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var response = await api.PostAsJsonAsync($"api/security-worker/heartbeat/{job.Id}",
                    new { job.LeaseToken, Progress=Volatile.Read(ref current) },timeout.Token);
                // Completion can commit just before the last heartbeat arrives. Do not turn a
                // successfully saved report into a client-side cancellation in that race.
                if (response.StatusCode==HttpStatusCode.Conflict && Volatile.Read(ref current).Stage=="SavingResults") return;
                ScannerHttp.RequireSuccess(response,"ProjectTracking heartbeat");
                await Task.Delay(1000,stop); // Coalesce transfer updates; at most one request per second.
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) when (Volatile.Read(ref current).Stage == "SavingResults"
            && error is HttpRequestException or OperationCanceledException)
        {
            // Completion holds the scan row lock until its transaction finishes.
            // Let the completion request report its own outcome if the heartbeat times out.
            Console.Error.WriteLine($"Scan {job.Id}: heartbeat unavailable while saving results; awaiting completion response");
        }
        catch { Console.Error.WriteLine($"Scan {job.Id}: heartbeat unavailable; stopping scan");work.Cancel(); }
    }
    public void Dispose()=>changed.Dispose();
}
