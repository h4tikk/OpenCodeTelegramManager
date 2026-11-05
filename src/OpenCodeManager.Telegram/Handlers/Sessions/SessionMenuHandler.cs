using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class SessionMenuHandler(TelegramUi ui, IManagerDbFactory factory, NavigationService navigation, SessionManager sessions) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "hide",
        "history",
        "newsession",
        "rename",
        "session",
        "sessions"
    };

    public async Task SessionsAsync(int page, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var list = await db.Sessions.Where(x => !x.Hidden).OrderByDescending(x => x.IsMain).ThenBy(x => x.Title).Skip(page * 8).Take(9).ToListAsync(ct);
        var rows = list.Take(8).Select(x => new[] { B(x.Title[..Math.Min(55, x.Title.Length)], "session", x.Id.ToString("N")) }).ToList();
        Pages(rows, "sessions", page, list.Count > 8);
        await SendTextAsync("Сессии", new(rows), ct);
    }

    public async Task SessionAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var s = await db.Sessions.SingleAsync(x => x.Id == id, ct);
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        nav.SelectedSessionId = id;
        nav.SelectedProjectId = s.ProjectId;
        await navigation.SaveNavigationAsync(nav, ct);
        await SendTextAsync($"{s.Title}\n{s.WorkingDirectory}\nМодель: {s.ProviderId}/{s.ModelId}\nАгент: {s.AgentName}", new(new[] { new[] { B("Написать задачу", "newtask", "0"), B("История", "history", "0") }, new[] { B("Модель", "models", "0"), B("Агент", "agents", "0") }, new[] { B("Переименовать", "rename", "0"), B("Скрыть локально", "hide", "0") }, new[] { B("Меню", "home", "0") } }), ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string arg, int messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "sessions":
                await SessionsAsync(Page(arg), ct);
                return true;
            case "session":
                if (!Guid.TryParseExact(arg, "N", out var sessionId))
                    return true;
                await SessionAsync(sessionId, ct);
                return true;
            case "history":
                if (nav.SelectedSessionId is Guid historyId)
                    await SendTextAsync(await sessions.HistoryAsync(historyId, ct), null, ct);
                return true;
            case "newsession":
                if (Guid.TryParseExact(arg, "N", out var newProject))
                {
                    await using var db = await factory.CreateAsync(ct);
                    if (!await db.Projects.AnyAsync(x => x.Id == newProject, ct))
                        return true;
                    nav.SelectedProjectId = newProject;
                    nav.InputMode = "sessiontitle";
                    await navigation.SaveNavigationAsync(nav, ct);
                    await SendTextAsync("Введите название независимой сессии.", null, ct);
                }

                return true;
            case "rename":
                nav.InputMode = "rename";
                await navigation.SaveNavigationAsync(nav, ct);
                await SendTextAsync("Введите новое название сессии.", null, ct);
                return true;
            case "hide":
                if (nav.SelectedSessionId is Guid hideId)
                    await sessions.HideAsync(hideId, security.OwnerUserId, ct);
                await SessionsAsync(0, ct);
                return true;
        }

        return false;
    }
}
