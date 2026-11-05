using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class TaskManager(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, OwnerGuard guard, ILogger<TaskManager> logger)
{
    public async Task<AgentTask> EnqueueAsync(Guid sessionId, string prompt, string deduplicationKey, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 100_000)
            throw new ArgumentException("Prompt must be 1–100000 characters");
        await using var db = await factory.CreateAsync(ct);
        var duplicate = await db.Tasks.SingleOrDefaultAsync(x => x.DeduplicationKey == deduplicationKey, ct);
        if (duplicate is not null)
            return duplicate;
        var session = await db.Sessions.SingleAsync(x => x.Id == sessionId, ct);
        paths.DemandAllowed(session.WorkingDirectory);
        var nav = await db.Navigation.SingleOrDefaultAsync(x => x.UserId == owner, ct);
        var t = new AgentTask
        {
            SessionId = sessionId,
            Title = prompt[..Math.Min(80, prompt.Length)],
            Prompt = prompt,
            DeduplicationKey = deduplicationKey,
            LockKey = paths.LockKey(session.WorkingDirectory),
            CreatedByTelegramUserId = owner,
            ProviderId = nav?.DraftProviderId ?? session.ProviderId ?? nav?.DefaultProviderId,
            ModelId = nav?.DraftModelId ?? session.ModelId ?? nav?.DefaultModelId,
            AgentName = nav?.DraftAgent ?? session.AgentName ?? nav?.DefaultAgent
        };
        db.Tasks.Add(t);
        var fileIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(nav?.PendingFileIdsJson ?? "[]") ?? [];
        foreach (var fileId in fileIds.Distinct())
        {
            var file = await db.Attachments.SingleAsync(x => x.Id == fileId && x.ExpiresAtUtc > DateTime.UtcNow, ct);
            db.TaskFiles.Add(new TaskFile { TaskId = t.Id, AttachmentId = fileId });
            file.TaskId ??= t.Id;
        }

        if (nav is not null)
        {
            nav.PendingFileIdsJson = null;
            nav.DraftProviderId = null;
            nav.DraftModelId = null;
            nav.DraftAgent = null;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var replay = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.DeduplicationKey == deduplicationKey, ct);
            if (replay is not null)
                return replay;
            throw;
        }

        logger.LogInformation("Task {TaskId} queued", t.Id);
        return t;
    }

    public async Task CancelAsync(Guid taskId, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await using var db = await factory.CreateAsync(ct);
        var t = await db.Tasks.SingleAsync(x => x.Id == taskId, ct);
        if (TaskStates.IsTerminal(t.Status))
            return;
        if (t.Status == AgentTaskStatus.Queued)
        {
            await db.Tasks.Where(x => x.Id == taskId && x.Status == AgentTaskStatus.Queued).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, AgentTaskStatus.Cancelled).SetProperty(t => t.CompletedAtUtc, DateTime.UtcNow), ct);
        }

        await db.Tasks.Where(x => x.Id == taskId && x.Status != AgentTaskStatus.Completed && x.Status != AgentTaskStatus.Failed && x.Status != AgentTaskStatus.Cancelled).ExecuteUpdateAsync(x => x.SetProperty(t => t.CancelRequested, true), ct);
    }

    public async Task<string> DiffAsync(Guid taskId, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var t = await db.Tasks.SingleAsync(x => x.Id == taskId, ct);
        var s = await db.Sessions.SingleAsync(x => x.Id == t.SessionId, ct);
        paths.DemandAllowed(s.WorkingDirectory);
        var attempt = await db.Attempts.Where(x => x.TaskId == taskId).OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(ct);
        return string.Join("\n", (await api.GetDiffAsync(s.OpenCodeSessionId, attempt?.OpenCodeMessageId, ct)).Select(x => x.File + "\n" + x.Patch));
    }
}
