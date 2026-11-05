using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;
using static OpenCodeManager.Application.TaskErrorFormatter;

namespace OpenCodeManager.Application;
public sealed class TaskDispatcher(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, TaskExecutionOptions options, ILogger<TaskDispatcher> logger, IFileManager? fileManager = null, ISessionAutonomy? autonomy = null)
{
    public async Task<bool> DispatchAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var task = await db.Tasks.SingleAsync(x => x.Id == id, ct);
        if (task.Status != AgentTaskStatus.Queued)
            return false;
        var session = await db.Sessions.SingleAsync(x => x.Id == task.SessionId, ct);
        var dir = paths.DemandAllowed(session.WorkingDirectory);
        if (paths.LockKey(dir) != task.LockKey)
            throw new InvalidOperationException("Project lock identity changed; requeue with a fresh directory");
        var remote = await api.GetSessionAsync(session.OpenCodeSessionId, ct);
        if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != dir)
            throw new InvalidOperationException("Session moved outside selected directory");
        if ((remote.Time.Busy ?? 0) > (remote.Time.Idle ?? 0) || (await api.GetInboxAsync(session.OpenCodeSessionId, ct)).Count > 0)
            return false;
        // Also protect against work running in a different OpenCode session sharing this tree.
        string? cursor = null;
        do
        {
            var page = await api.GetSessionsAsync(cursor, ct);
            foreach (var other in page.Data.Where(x => (x.Time.Busy ?? 0) > (x.Time.Idle ?? 0) && x.Location.Directory is not null))
            {
                try
                {
                    if (paths.LockKey(other.Location.Directory!) == task.LockKey)
                        return false;
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            cursor = page.Cursor?.Next;
        }
        while (cursor is not null);
        task.Status = AgentTaskStatus.Dispatching;
        task.StartedAtUtc = DateTime.UtcNow;
        var attempt = new TaskAttempt
        {
            TaskId = id,
            AttemptNumber = await db.Attempts.CountAsync(x => x.TaskId == id, ct) + 1,
            OpenCodeMessageId = "msg_manager_" + Guid.NewGuid().ToString("N"),
            ProviderId = task.ProviderId,
            ModelId = task.ModelId,
            Status = AttemptStatus.Dispatching
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        bool sending = false;
        try
        {
            if (autonomy is not null)
                await autonomy.ConfigureAsync(remote, dir, ct);
            await api.UpdateSessionAsync(session.OpenCodeSessionId, null, null, SessionPermissionPolicy.CreateRules().Concat(remote.Permissions?.Where(x => x.Effect == "deny") ?? []).ToList(), ct);
            if (task.ModelId is not null && task.ProviderId is not null)
                await api.SwitchModelAsync(session.OpenCodeSessionId, new(task.ModelId, task.ProviderId), ct);
            if (task.AgentName is not null)
                await api.SwitchAgentAsync(session.OpenCodeSessionId, task.AgentName, ct);
            // The preceding durable transaction is the boundary: any ambiguous POST is never retried.
            var attachedFiles = fileManager is null ? null : await fileManager.PrepareTaskFilesAsync(task.Id, dir, task.ProviderId, task.ModelId, ct);
            if (await db.Tasks.AsNoTracking().Where(x => x.Id == task.Id).Select(x => x.CancelRequested).SingleAsync(ct))
            {
                task.Status = AgentTaskStatus.Cancelled;
                attempt.Status = AttemptStatus.Cancelled;
                task.CompletedAtUtc = DateTime.UtcNow;
                attempt.CompletedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return false;
            }

            sending = true;
            var input = await api.SendPromptAsync(session.OpenCodeSessionId, new(attempt.OpenCodeMessageId!, task.Prompt + (autonomy?.Enabled != true ? "" : "\n\nРаботай самостоятельно в выбранном проекте. Политика разрешений оценивает действия автоматически: не проси владельца подтвердить обычные операции и не обходи отказ. При недоступной зависимости или ограничении среды укажи конкретный блокер. Shell изолирован, без сети, с локальными кэшами зависимостей; изменения обычных файлов возвращаются в проект, конфигурация OpenCode и секреты исключены.") + "\n\nПроверяй результат сборкой и тестами. Исправляй ошибки, не скрывай неуспешные проверки. Максимум циклов проверки: " + options.MaxVerificationCycles + ".", new() { ["managerTaskId"] = task.Id.ToString() }, attachedFiles), ct);
            if (input.Id != attempt.OpenCodeMessageId)
                throw new InvalidOperationException("Server changed client message ID");
            task.Status = AgentTaskStatus.Running;
            task.CurrentAction = "Промпт принят OpenCode";
            attempt.Status = AttemptStatus.Running;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Task {TaskId} admitted as {MessageId}", task.Id, input.Id);
            return true;
        }
        catch (Exception e)
        {
            var knownRejected = e is OpenCodeApiException { StatusCode: 400 or 401 or 403 or 404 or 429 };
            task.Status = sending && !knownRejected ? AgentTaskStatus.NeedsReconciliation : AgentTaskStatus.Failed;
            attempt.Status = task.Status == AgentTaskStatus.Failed ? AttemptStatus.Failed : AttemptStatus.Uncertain;
            task.LastError = SafeError(e);
            attempt.ErrorMessage = task.LastError;
            attempt.ErrorCode = e is OpenCodeApiException apiError ? apiError.Code : e.GetType().Name;
            if (task.Status == AgentTaskStatus.Failed)
            {
                task.CompletedAtUtc = DateTime.UtcNow;
                attempt.CompletedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(CancellationToken.None);
            if (ct.IsCancellationRequested)
                throw;
            return task.Status == AgentTaskStatus.NeedsReconciliation;
        }
    }
}
