using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using static OpenCodeManager.Telegram.TaskMessageFormatter;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class TaskMenuHandler(TelegramUi ui, IManagerDbFactory factory, NavigationService navigation, TaskManager tasks, SessionMenuHandler sessionScreens) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "diff",
        "logs",
        "newtask",
        "result",
        "sessiontask",
        "stop",
        "task",
        "tasks"
    };

    public async Task TasksAsync(int page, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var list = await db.Tasks.OrderByDescending(x => x.CreatedAtUtc).Skip(page * 8).Take(9).ToListAsync(ct);
        var rows = list.Take(8).Select(x => new[] { B(x.Status + " " + x.Id.ToString("N")[..8] + " " + x.Title[..Math.Min(25, x.Title.Length)], "task", x.Id.ToString("N")) }).ToList();
        Pages(rows, "tasks", page, list.Count > 8);
        await SendTextAsync("Задачи", new(rows), ct);
    }

    public async Task TaskCardAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var task = await db.Tasks.SingleAsync(x => x.Id == id, ct);
        var session = await db.Sessions.SingleAsync(x => x.Id == task.SessionId, ct);
        await SendTextAsync(TaskText(task, session), TaskButtons(task), ct);
    }

    public async Task SubmitAsync(NavigationState nav, string input, int updateId, CancellationToken ct)
    {
        if (nav.SelectedSessionId is not Guid sessionId)
        {
            await SendTextAsync("Сначала выберите сессию.", Menu, ct);
            return;
        }

        if (nav.PendingAttachmentText is not null)
            input += "\n\n" + nav.PendingAttachmentText;
        var task = await tasks.EnqueueAsync(sessionId, input, "telegram:" + updateId, security.OwnerUserId, ct);
        nav.InputMode = null;
        nav.DraftProviderId = null;
        nav.DraftModelId = null;
        nav.DraftAgent = null;
        nav.PendingAttachmentText = null;
        nav.PendingFileIdsJson = null;
        await navigation.SaveNavigationAsync(nav, ct);
        await TaskCardAsync(task.Id, ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string arg, int messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "tasks":
                await TasksAsync(Page(arg), ct);
                return true;
            case "task":
                if (Guid.TryParseExact(arg, "N", out var taskId))
                    await TaskCardAsync(taskId, ct);
                return true;
            case "stop":
                if (Guid.TryParseExact(arg, "N", out var stopId))
                {
                    await tasks.CancelAsync(stopId, security.OwnerUserId, ct);
                    await TaskCardAsync(stopId, ct);
                }

                return true;
            case "result":
                if (Guid.TryParseExact(arg, "N", out var resultId))
                {
                    await using var db = await factory.CreateAsync(ct);
                    var task = await db.Tasks.SingleAsync(x => x.Id == resultId, ct);
                    await SendTextAsync(task.ResultPreview ?? task.LastError ?? "Результат пока отсутствует.", null, ct);
                }

                return true;
            case "logs":
                if (Guid.TryParseExact(arg, "N", out var logsId))
                {
                    await using var db = await factory.CreateAsync(ct);
                    var attemptIds = await db.Attempts.Where(x => x.TaskId == logsId).Select(x => x.Id).ToListAsync(ct);
                    var events = await db.Events.Where(x => attemptIds.Contains(x.AttemptId)).OrderBy(x => x.OccurredAtUtc).ToListAsync(ct);
                    await SendTextAsync(string.Join("\n", events.Select(x => x.OccurredAtUtc.ToString("HH:mm:ss") + " " + x.Summary))is { Length: > 0 } log ? log : "Событий пока нет.", null, ct);
                }

                return true;
            case "diff":
                if (Guid.TryParseExact(arg, "N", out var diffId))
                    await SendTextAsync(await tasks.DiffAsync(diffId, ct)is { Length: > 0 } diff ? diff : "Изменения не зафиксированы OpenCode.", null, ct);
                return true;
            case "newtask":
                nav.InputMode = "prompt";
                await navigation.SaveNavigationAsync(nav, ct);
                await SendTextAsync(nav.SelectedSessionId is null ? "Сначала выберите сессию." : "Напишите задачу. Можно прикрепить файл с подписью.", Menu, ct);
                return true;
            case "sessiontask":
                if (Guid.TryParseExact(arg, "N", out var continueId))
                {
                    await using var db = await factory.CreateAsync(ct);
                    var task = await db.Tasks.SingleAsync(x => x.Id == continueId, ct);
                    await sessionScreens.SessionAsync(task.SessionId, ct);
                }

                return true;
        }

        return false;
    }
}
