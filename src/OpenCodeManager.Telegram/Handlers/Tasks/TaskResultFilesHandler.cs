using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using Telegram.Bot.Types.ReplyMarkups;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class TaskResultFilesHandler(TelegramUi ui, IManagerDbFactory factory, IOpenCodeClient api, IFileManager fileManager, NavigationService navigation) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "resultfiles"
    };

    public async Task ResultFilesAsync(Guid taskId, int? messageId, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var task = await db.Tasks.SingleAsync(x => x.Id == taskId, ct);
        var session = await db.Sessions.SingleAsync(x => x.Id == task.SessionId, ct);
        var attempt = await db.Attempts.Where(x => x.TaskId == taskId).OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(ct);
        if (attempt?.OpenCodeMessageId is null)
        {
            await SendTextAsync("Нет принятой попытки выполнения.", null, ct);
            return;
        }

        var changed = await api.GetDiffAsync(session.OpenCodeSessionId, attempt.OpenCodeMessageId, ct);
        var rows = new List<InlineKeyboardButton[]>();
        foreach (var item in changed.Take(30))
        {
            try
            {
                var info = await fileManager.InspectAsync(Path.Combine(session.WorkingDirectory, item.File), security.OwnerUserId, ct);
                rows.Add([B(info.Name, "file", StoreChoice("file", info.CanonicalPath, null, null))]);
            }
            catch (Exception e)when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }
        }

        rows.Add([B("Карточка задачи", "task", taskId.ToString("N"))]);
        await RenderBrowserAsync("Файлы из снимков OpenCode, проверенные на диске. Выберите конкретные файлы для скачивания или ZIP; изменения пользователя автоматически не включаются.", new(rows), messageId, ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string argument, int messageId, CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "resultfiles":
                if (!Guid.TryParseExact(argument, "N", out var taskId))
                    return true;
                await ResultFilesAsync(taskId, messageId, ct);
                return true;
        }

        return false;
    }
}
