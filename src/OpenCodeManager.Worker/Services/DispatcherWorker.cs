using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Application;

namespace OpenCodeManager.Worker;
public sealed class DispatcherWorker(TaskEngine engine, TaskExecutionOptions options, ILogger<DispatcherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await engine.RecoverAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await engine.TickAsync(ct);
            }
            catch (OperationCanceledException)when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning("Dispatcher tick failed: {ErrorType}", e.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), ct);
        }
    }
}
