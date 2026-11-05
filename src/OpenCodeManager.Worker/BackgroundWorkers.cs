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
            try { await engine.TickAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Dispatcher tick failed: {ErrorType}", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds),ct);
        }
    }
}
public sealed class OpenCodeEventListener(IOpenCodeClient api, TaskEngine engine, ILogger<OpenCodeEventListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int failures=0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // REST reconciliation is performed continuously by DispatcherWorker, including after every reconnect.
                await foreach(var ev in api.SubscribeAsync(ct)) { failures=0; await engine.RecordEventAsync(ev,ct); }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { break; }
            catch(Exception e) { logger.LogWarning("OpenCode SSE disconnected: {ErrorType}",e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,Math.Pow(2,Math.Min(++failures,5)))),ct);
        }
    }
}
