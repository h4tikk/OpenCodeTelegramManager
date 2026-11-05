using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class TaskReconciler(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, PermissionManager permissions, TaskExecutionOptions options, ILogger<TaskReconciler> logger)
{
    public async Task ReconcileAsync(Guid taskId, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var task = await db.Tasks.SingleAsync(x => x.Id == taskId, ct);
        if (!TaskStates.IsActive(task.Status))
            return;
        var session = await db.Sessions.SingleAsync(x => x.Id == task.SessionId, ct);
        paths.DemandAllowed(session.WorkingDirectory);
        var attempt = await db.Attempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(x => x.TaskId == task.Id, ct);
        if (attempt?.OpenCodeMessageId is null)
        {
            task.Status = AgentTaskStatus.NeedsReconciliation;
            task.LastError = "No durable attempt ID; automatic execution disabled";
            await db.SaveChangesAsync(ct);
            return;
        }

        var remote = await api.GetSessionAsync(session.OpenCodeSessionId, ct);
        if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != session.WorkingDirectory)
        {
            task.Status = AgentTaskStatus.NeedsReconciliation;
            task.LastError = "Remote session location changed";
            await db.SaveChangesAsync(ct);
            return;
        }

        var messages = new List<OpenCodeMessage>();
        string? cursor = null;
        var seen = new HashSet<string>();
        do
        {
            var page = await api.GetMessagesAsync(session.OpenCodeSessionId, cursor, ct);
            messages.AddRange(page.Data);
            cursor = page.Cursor?.Next;
            if (messages.Any(x => x.Id == attempt.OpenCodeMessageId))
                break;
        }
        while (cursor is not null && seen.Add(cursor) && messages.Count < 10_000);
        var ordered = messages.OrderBy(x => x.Time.Created).ToList();
        int start = ordered.FindIndex(x => x.Id == attempt.OpenCodeMessageId);
        var inbox = await api.GetInboxAsync(session.OpenCodeSessionId, ct);
        if (start < 0)
        {
            if (inbox.Any(x => x.Id == attempt.OpenCodeMessageId))
            {
                if (task.CancelRequested || DateTime.UtcNow - task.StartedAtUtc > TimeSpan.FromMinutes(options.MaxDurationMinutes))
                {
                    await api.CancelInboxAsync(session.OpenCodeSessionId, attempt.OpenCodeMessageId, ct);
                    task.Status = AgentTaskStatus.Cancelled;
                    attempt.Status = AttemptStatus.Cancelled;
                    task.CompletedAtUtc = DateTime.UtcNow;
                    attempt.CompletedAtUtc = DateTime.UtcNow;
                }
                else
                {
                    task.Status = AgentTaskStatus.Running;
                    attempt.Status = AttemptStatus.Running;
                }
            }
            else
            {
                task.Status = AgentTaskStatus.NeedsReconciliation;
                task.LastError = "Prompt admission unconfirmed; no automatic resend";
            }

            await db.SaveChangesAsync(ct);
            return;
        }

        var turn = ordered.Skip(start + 1).ToList();
        var idle = turn.FirstOrDefault(x => x.Type == "idle");
        var otherPrompt = turn.FirstOrDefault(x => x.Type == "user");
        if (otherPrompt is not null && (idle is null || otherPrompt.Time.Created < idle.Time.Created))
        {
            task.Status = AgentTaskStatus.NeedsReconciliation;
            task.LastError = "External prompt entered the same turn; manual review required";
            await db.SaveChangesAsync(ct);
            return;
        }

        if (idle is not null)
        {
            task.Status = idle.Outcome switch
            {
                "succeeded" => AgentTaskStatus.Completed,
                "interrupted" => AgentTaskStatus.Cancelled,
                "failed" => AgentTaskStatus.Failed,
                _ => AgentTaskStatus.NeedsReconciliation
            };
            if (TaskStates.IsTerminal(task.Status))
            {
                attempt.Status = task.Status switch
                {
                    AgentTaskStatus.Completed => AttemptStatus.Completed,
                    AgentTaskStatus.Cancelled => AttemptStatus.Cancelled,
                    _ => AttemptStatus.Failed
                };
                var result = string.Join("\n\n", turn.TakeWhile(x => x.Id != idle.Id).Where(x => x.Type == "assistant").SelectMany(x => x.Content ?? []).Where(x => x.Type == "text").Select(x => x.Text));
                var files = turn.SelectMany(x => x.Snapshot?.Files ?? []).Distinct().ToArray();
                task.ResultPreview = result.Length > 100_000 ? result[^100_000..] : result;
                if (files.Length > 0)
                    task.ResultPreview += "\n\nИзменённые файлы:\n" + string.Join("\n", files);
                task.LastError = idle.Error is null ? null : idle.Error.Type + (idle.Error.Status is int status ? $" (HTTP {status})" : "");
                task.CompletedAtUtc = DateTime.UtcNow;
                attempt.CompletedAtUtc = DateTime.UtcNow;
                task.CurrentAction = "Завершено: " + idle.Outcome;
                logger.LogInformation("Task {TaskId} finished: {Status}", task.Id, task.Status);
            }

            await db.SaveChangesAsync(ct);
            return;
        }

        task.Status = AgentTaskStatus.Running;
        attempt.Status = AttemptStatus.Running;
        var toolCalls = task.ObservedToolCalls;
        bool timedOut = DateTime.UtcNow - task.StartedAtUtc > TimeSpan.FromMinutes(options.MaxDurationMinutes);
        if (task.CancelRequested || timedOut || toolCalls >= options.MaxToolCalls)
        {
            await api.InterruptAsync(session.OpenCodeSessionId, ct);
            task.CancelRequested = true;
            task.CurrentAction = timedOut ? "Запрошена остановка: лимит времени" : "Запрошена остановка";
        }

        await db.SaveChangesAsync(ct);
        await permissions.SyncAsync(task, session, ct);
        bool pending = await db.Permissions.AnyAsync(x => x.TaskId == task.Id && x.Status == ApprovalStatus.Pending, ct);
        if (pending)
        {
            task.Status = AgentTaskStatus.AwaitingApproval;
            task.CurrentAction = "Ожидание разрешения владельца";
            await db.SaveChangesAsync(ct);
        }
    }
}
