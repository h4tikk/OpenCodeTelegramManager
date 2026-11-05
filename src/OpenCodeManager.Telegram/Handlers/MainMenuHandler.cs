using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using static OpenCodeManager.Telegram.TelegramErrors;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class MainMenuHandler(TelegramUi ui, IOpenCodeClient api, IManagerDbFactory factory, TaskEngine engine, NavigationService navigation) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "home"
    };

    public async Task HomeAsync(CancellationToken ct)
    {
        string server;
        try
        {
            server = "ONLINE " + (await api.GetInfoAsync(ct)).Version;
        }
        catch (Exception e)
        {
            server = "OFFLINE (" + SafeError(e) + ")";
        }

        await using var db = await factory.CreateAsync(ct);
        var active = await db.Tasks.CountAsync(x => x.Status == AgentTaskStatus.Running || x.Status == AgentTaskStatus.AwaitingApproval, ct);
        var queued = await db.Tasks.CountAsync(x => x.Status == AgentTaskStatus.Queued, ct);
        var uncertain = await db.Tasks.CountAsync(x => x.Status == AgentTaskStatus.NeedsReconciliation, ct);
        await SendTextAsync($"OpenCode Manager\n\nСервер: {server}\nSQLite: OK\nАктивные задачи: {active}\nВ очереди: {queued}\nТребуют сверки: {uncertain}\nDispatcher: {(engine.LastSuccessfulTickUtc is DateTime last && DateTime.UtcNow - last < TimeSpan.FromSeconds(30) ? "ONLINE" : "ожидание / сбой")}\nПоследняя ошибка: {engine.LastError ?? "нет"}", Menu, ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string arg, int messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "home":
                await HomeAsync(ct);
                return true;
        }

        return false;
    }
}
