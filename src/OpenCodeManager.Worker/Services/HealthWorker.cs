using Microsoft.Extensions.Hosting;
using OpenCodeManager.Application;

namespace OpenCodeManager.Worker;
public sealed class HealthWorker(StorageOptions storage) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!, "worker.heartbeat"), DateTime.UtcNow.ToString("O"), ct);
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}
