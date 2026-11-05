using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;
namespace OpenCodeManager.Application;

public sealed class TaskEngine(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, PermissionManager permissions, TaskExecutionOptions options, ILogger<TaskEngine> logger)
{
    private readonly SemaphoreSlim gate = new(1);
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
            foreach (var id in active)
            {
                try { await ReconcileAsync(id, ct); }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) { LastError = SafeError(e); logger.LogWarning("Task {TaskId} reconciliation failed: {ErrorType}", id, e.GetType().Name); }
            }
            int count = await db.Tasks.CountAsync(x => x.Status == AgentTaskStatus.Dispatching || x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval || x.Status == AgentTaskStatus.NeedsReconciliation, ct);
            var queue = await db.Tasks.AsNoTracking().Where(x => x.Status == AgentTaskStatus.Queued).OrderBy(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
            foreach (var task in queue)
            {
                if (count >= options.MaxConcurrentTasks) break;
                if (await db.Tasks.AnyAsync(x => x.LockKey == task.LockKey && (x.Status == AgentTaskStatus.Dispatching || x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval || x.Status == AgentTaskStatus.NeedsReconciliation), ct)) continue;
                if (await DispatchAsync(task.Id, ct)) count++;
            }
            LastSuccessfulTickUtc = DateTime.UtcNow; LastError = null;
        }
        finally { gate.Release(); }
    }
    private async Task<bool> DispatchAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var task = await db.Tasks.SingleAsync(x => x.Id == id, ct); if (task.Status != AgentTaskStatus.Queued) return false;
        var session = await db.Sessions.SingleAsync(x => x.Id == task.SessionId, ct);
        var dir = paths.DemandAllowed(session.WorkingDirectory);
        if (paths.LockKey(dir) != task.LockKey) throw new InvalidOperationException("Project lock identity changed; requeue with a fresh directory");
        var remote = await api.GetSessionAsync(session.OpenCodeSessionId, ct);
        if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != dir) throw new InvalidOperationException("Session moved outside selected directory");
        if ((remote.Time.Busy ?? 0) > (remote.Time.Idle ?? 0) || (await api.GetInboxAsync(session.OpenCodeSessionId, ct)).Count > 0) return false;
        // Also protect against work running in a different OpenCode session sharing this tree.
        string? cursor = null;
        do
        {
            var page = await api.GetSessionsAsync(cursor, ct);
            foreach (var other in page.Data.Where(x => (x.Time.Busy ?? 0) > (x.Time.Idle ?? 0) && x.Location.Directory is not null))
            {
                try { if (paths.LockKey(other.Location.Directory!) == task.LockKey) return false; }
                catch (UnauthorizedAccessException) { }
            }
            cursor = page.Cursor?.Next;
        } while (cursor is not null);
        task.Status = AgentTaskStatus.Dispatching; task.StartedAtUtc = DateTime.UtcNow;
        var attempt = new TaskAttempt { TaskId = id, AttemptNumber = await db.Attempts.CountAsync(x => x.TaskId == id, ct) + 1, OpenCodeMessageId = "msg_manager_" + Guid.NewGuid().ToString("N"), ProviderId = task.ProviderId, ModelId = task.ModelId, Status = AttemptStatus.Dispatching };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        bool sending = false;
        try
        {
            await api.UpdateSessionAsync(session.OpenCodeSessionId, null, null, ManagerService.SessionRules(), ct);
            if (task.ModelId is not null && task.ProviderId is not null) await api.SwitchModelAsync(session.OpenCodeSessionId, new(task.ModelId, task.ProviderId), ct);
            if (task.AgentName is not null) await api.SwitchAgentAsync(session.OpenCodeSessionId, task.AgentName, ct);
            // The preceding durable transaction is the boundary: any ambiguous POST is never retried.
            sending = true;
            var input = await api.SendPromptAsync(session.OpenCodeSessionId, new(attempt.OpenCodeMessageId!, task.Prompt + "\n\nПроверяй результат сборкой и тестами. Исправляй ошибки, не скрывай неуспешные проверки. Максимум циклов проверки: " + options.MaxVerificationCycles + ".", new() { ["managerTaskId"] = task.Id.ToString() }), ct);
            if (input.Id != attempt.OpenCodeMessageId) throw new InvalidOperationException("Server changed client message ID");
            task.Status = AgentTaskStatus.Running; task.CurrentAction = "Промпт принят OpenCode"; attempt.Status = AttemptStatus.Running;
            await db.SaveChangesAsync(ct); logger.LogInformation("Task {TaskId} admitted as {MessageId}", task.Id, input.Id); return true;
        }
        catch (Exception e)
        {
            var knownRejected = e is OpenCodeApiException { StatusCode: 400 or 401 or 403 or 404 or 429 };
            task.Status = sending && !knownRejected ? AgentTaskStatus.NeedsReconciliation : AgentTaskStatus.Failed;
            attempt.Status = task.Status == AgentTaskStatus.Failed ? AttemptStatus.Failed : AttemptStatus.Uncertain;
            task.LastError = SafeError(e); attempt.ErrorMessage = task.LastError;
            attempt.ErrorCode = e is OpenCodeApiException apiError ? apiError.Code : e.GetType().Name;
            if (task.Status == AgentTaskStatus.Failed) { task.CompletedAtUtc = DateTime.UtcNow; attempt.CompletedAtUtc = DateTime.UtcNow; }
            await db.SaveChangesAsync(CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            return task.Status == AgentTaskStatus.NeedsReconciliation;
        }
    }
    public async Task ReconcileAsync(Guid taskId, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var task = await db.Tasks.SingleAsync(x => x.Id == taskId, ct); if (!TaskStates.IsActive(task.Status)) return;
        var session = await db.Sessions.SingleAsync(x => x.Id == task.SessionId, ct); paths.DemandAllowed(session.WorkingDirectory);
        var attempt = await db.Attempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(x => x.TaskId == task.Id, ct);
        if (attempt?.OpenCodeMessageId is null) { task.Status = AgentTaskStatus.NeedsReconciliation; task.LastError = "No durable attempt ID; automatic execution disabled"; await db.SaveChangesAsync(ct); return; }
        var remote = await api.GetSessionAsync(session.OpenCodeSessionId, ct);
        if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != session.WorkingDirectory) { task.Status = AgentTaskStatus.NeedsReconciliation; task.LastError = "Remote session location changed"; await db.SaveChangesAsync(ct); return; }
        var messages = new List<OpenCodeMessage>(); string? cursor = null; var seen = new HashSet<string>();
        do
        {
            var page = await api.GetMessagesAsync(session.OpenCodeSessionId, cursor, ct); messages.AddRange(page.Data);
            cursor = page.Cursor?.Next;
            if (messages.Any(x => x.Id == attempt.OpenCodeMessageId)) break;
        } while (cursor is not null && seen.Add(cursor) && messages.Count < 10_000);
        var ordered = messages.OrderBy(x => x.Time.Created).ToList();
        int start = ordered.FindIndex(x => x.Id == attempt.OpenCodeMessageId);
        var inbox = await api.GetInboxAsync(session.OpenCodeSessionId, ct);
        if (start < 0)
        {
            if (inbox.Any(x => x.Id == attempt.OpenCodeMessageId)) { task.Status = AgentTaskStatus.Running; attempt.Status = AttemptStatus.Running; }
            else { task.Status = AgentTaskStatus.NeedsReconciliation; task.LastError = "Prompt admission unconfirmed; no automatic resend"; }
            await db.SaveChangesAsync(ct); return;
        }
        var turn = ordered.Skip(start + 1).ToList();
        var idle = turn.FirstOrDefault(x => x.Type == "idle");
        var otherPrompt = turn.FirstOrDefault(x => x.Type == "user");
        if (otherPrompt is not null && (idle is null || otherPrompt.Time.Created < idle.Time.Created))
        {
            task.Status = AgentTaskStatus.NeedsReconciliation; task.LastError = "External prompt entered the same turn; manual review required"; await db.SaveChangesAsync(ct); return;
        }
        if (idle is not null)
        {
            task.Status = idle.Outcome switch { "succeeded" => AgentTaskStatus.Completed, "interrupted" => AgentTaskStatus.Cancelled, "failed" => AgentTaskStatus.Failed, _ => AgentTaskStatus.NeedsReconciliation };
            if (TaskStates.IsTerminal(task.Status))
            {
                attempt.Status = task.Status switch { AgentTaskStatus.Completed => AttemptStatus.Completed, AgentTaskStatus.Cancelled => AttemptStatus.Cancelled, _ => AttemptStatus.Failed };
                var result = string.Join("\n\n", turn.TakeWhile(x => x.Id != idle.Id).Where(x => x.Type == "assistant").SelectMany(x => x.Content ?? []).Where(x => x.Type == "text").Select(x => x.Text));
                var files = turn.SelectMany(x => x.Snapshot?.Files ?? []).Distinct().ToArray();
                task.ResultPreview = result.Length > 100_000 ? result[^100_000..] : result;
                if (files.Length > 0) task.ResultPreview += "\n\nИзменённые файлы:\n" + string.Join("\n", files);
                task.LastError = idle.Error is null ? null : idle.Error.Type + (idle.Error.Status is int status ? $" (HTTP {status})" : "");
                task.CompletedAtUtc = DateTime.UtcNow; attempt.CompletedAtUtc = DateTime.UtcNow; task.CurrentAction = "Завершено: " + idle.Outcome;
                logger.LogInformation("Task {TaskId} finished: {Status}", task.Id, task.Status);
            }
            await db.SaveChangesAsync(ct); return;
        }
        task.Status = AgentTaskStatus.Running; attempt.Status = AttemptStatus.Running;
        var toolCalls = await db.Events.CountAsync(x => x.AttemptId == attempt.Id && x.EventType == "session.tool.called", ct);
        bool timedOut = DateTime.UtcNow - task.StartedAtUtc > TimeSpan.FromMinutes(options.MaxDurationMinutes);
        if (task.CancelRequested || timedOut || toolCalls >= options.MaxToolCalls)
        {
            await api.InterruptAsync(session.OpenCodeSessionId, ct);
            task.CancelRequested = true; task.CurrentAction = timedOut ? "Запрошена остановка: лимит времени" : "Запрошена остановка";
        }
        await db.SaveChangesAsync(ct);
        await permissions.SyncAsync(task, session, ct);
        bool pending = await db.Permissions.AnyAsync(x => x.TaskId == task.Id && x.Status == ApprovalStatus.Pending, ct);
        if (pending) { task.Status = AgentTaskStatus.AwaitingApproval; task.CurrentAction = "Ожидание разрешения владельца"; await db.SaveChangesAsync(ct); }
    }
    public async Task RecordEventAsync(OpenCodeEvent ev, CancellationToken ct)
    {
        if (ev.Type.Contains(".delta", StringComparison.Ordinal) || ev.Type is "session.step.streamed" or "session.tool.progress") return;
        if (ev.Data.ValueKind != System.Text.Json.JsonValueKind.Object || !ev.Data.TryGetProperty("sessionID", out var sid)) return;
        var remoteId = sid.GetString();
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.OpenCodeSessionId == remoteId, ct); if (session is null) return;
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.SessionId == session.Id && (x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval || x.Status == AgentTaskStatus.NeedsReconciliation), ct); if (task is null) return;
            var attempt = await db.Attempts.OrderByDescending(x => x.AttemptNumber).FirstAsync(x => x.TaskId == task.Id, ct);
            if (ev.Id is not null && await db.Events.AnyAsync(x => x.AttemptId == attempt.Id && x.OpenCodeEventId == ev.Id, ct)) return;
            string summary = ev.Type;
            if (ev.Data.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String) summary += ": " + name.GetString();
            if (ev.Data.TryGetProperty("native", out var native) && native.ValueKind == System.Text.Json.JsonValueKind.Object && native.TryGetProperty("name", out var tool)) summary += ": " + tool.GetString();
            task.CurrentAction = summary;
            db.Events.Add(new TaskEvent { AttemptId = attempt.Id, OpenCodeEventId = ev.Id, EventType = ev.Type, Summary = summary });
            await db.SaveChangesAsync(ct);
            var overflow = await db.Events.Where(x => x.AttemptId == attempt.Id).OrderByDescending(x => x.OccurredAtUtc).Skip(options.MaxEventsPerAttempt).Select(x => x.Id).ToListAsync(ct);
            if (overflow.Count > 0) await db.Events.Where(x => overflow.Contains(x.Id)).ExecuteDeleteAsync(ct);
        }
        finally { gate.Release(); }
    }
    private static string SafeError(Exception e) => e is OpenCodeApiException apiError ? apiError.Message : e.GetType().Name;
}
