using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;
using static OpenCodeManager.Application.TaskErrorFormatter;

namespace OpenCodeManager.Application;
public sealed class TaskEngine(IManagerDbFactory factory, TaskDispatcher dispatcher, TaskReconciler reconciler, TaskEventRecorder events, TaskExecutionGate coordination, TaskExecutionOptions options, ILogger<TaskEngine> logger)
{
    public DateTime? LastSuccessfulTickUtc { get; private set; }
    public string? LastError { get; private set; }

    public async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        await db.Tasks.Where(x => x.Status == AgentTaskStatus.Dispatching).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AgentTaskStatus.NeedsReconciliation).SetProperty(x => x.LastError, "Restart during dispatch: checking admission; no automatic resend"), ct);
        await db.Attempts.Where(x => x.Status == AttemptStatus.Dispatching).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AttemptStatus.Uncertain), ct);
    }

    public async Task TickAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var active = await db.Tasks.Where(x => x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval || x.Status == AgentTaskStatus.NeedsReconciliation).Select(x => x.Id).ToListAsync(ct);
            bool reconcileFailed = false;
            foreach (var id in active)
            {
                try
                {
                    await reconciler.ReconcileAsync(id, ct);
                }
                catch (Exception e)when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    reconcileFailed = true;
                    LastError = SafeError(e);
                    logger.LogWarning("Task {TaskId} reconciliation failed: {ErrorType}", id, e.GetType().Name);
                }
            }

            int count = await db.Tasks.CountAsync(x => x.Status == AgentTaskStatus.Dispatching || x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval || x.Status == AgentTaskStatus.NeedsReconciliation, ct);
            var queue = await db.Tasks.AsNoTracking().Where(x => x.Status == AgentTaskStatus.Queued).OrderBy(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
            foreach (var task in queue)
            {
                if (count >= options.MaxConcurrentTasks)
                    break;
                if (await db.Tasks.AnyAsync(x => x.LockKey == task.LockKey && (x.Status == AgentTaskStatus.Dispatching || x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval || x.Status == AgentTaskStatus.NeedsReconciliation), ct))
                    continue;
                if (await dispatcher.DispatchAsync(task.Id, ct))
                    count++;
            }

            LastSuccessfulTickUtc = DateTime.UtcNow;
            if (!reconcileFailed)
                LastError = null;
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim gate => coordination.Gate;

    public Task ReconcileAsync(Guid taskId, CancellationToken ct) => reconciler.ReconcileAsync(taskId, ct);
    public Task RecordEventAsync(OpenCodeEvent ev, CancellationToken ct) => events.RecordEventAsync(ev, ct);
}
