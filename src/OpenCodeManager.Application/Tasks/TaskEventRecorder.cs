using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class TaskEventRecorder(IManagerDbFactory factory, TaskExecutionOptions options, TaskExecutionGate coordination)
{
    private SemaphoreSlim gate => coordination.Gate;

    public async Task RecordEventAsync(OpenCodeEvent ev, CancellationToken ct)
    {
        if (ev.Type.Contains(".delta", StringComparison.Ordinal) || ev.Type is "session.step.streamed" or "session.tool.progress")
            return;
        if (ev.Data.ValueKind != System.Text.Json.JsonValueKind.Object || !ev.Data.TryGetProperty("sessionID", out var sid))
            return;
        var remoteId = sid.GetString();
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.OpenCodeSessionId == remoteId, ct);
            if (session is null)
                return;
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.SessionId == session.Id && (x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval || x.Status == AgentTaskStatus.NeedsReconciliation), ct);
            if (task is null)
                return;
            var attempt = await db.Attempts.OrderByDescending(x => x.AttemptNumber).FirstAsync(x => x.TaskId == task.Id, ct);
            if (ev.Id is not null && await db.Events.AnyAsync(x => x.AttemptId == attempt.Id && x.OpenCodeEventId == ev.Id, ct))
                return;
            string summary = ev.Type;
            if (ev.Data.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String)
                summary += ": " + name.GetString();
            if (ev.Data.TryGetProperty("native", out var native) && native.ValueKind == System.Text.Json.JsonValueKind.Object && native.TryGetProperty("name", out var tool))
                summary += ": " + tool.GetString();
            task.CurrentAction = summary;
            if (ev.Type == "session.tool.called")
                task.ObservedToolCalls++;
            db.Events.Add(new TaskEvent { AttemptId = attempt.Id, OpenCodeEventId = ev.Id, EventType = ev.Type, Summary = summary });
            await db.SaveChangesAsync(ct);
            var overflow = await db.Events.Where(x => x.AttemptId == attempt.Id).OrderByDescending(x => x.OccurredAtUtc).Skip(options.MaxEventsPerAttempt).Select(x => x.Id).ToListAsync(ct);
            if (overflow.Count > 0)
                await db.Events.Where(x => overflow.Contains(x.Id)).ExecuteDeleteAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }
}
