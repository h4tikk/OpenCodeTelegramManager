using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class ProjectMenuHandler(TelegramUi ui, IManagerDbFactory factory, NavigationService navigation) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "project",
        "projects"
    };

    public async Task ProjectsAsync(int page, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var list = await db.Projects.OrderBy(x => x.AbsolutePath).Skip(page * 8).Take(9).ToListAsync(ct);
        var rows = list.Take(8).Select(x => new[] { B((x.Kind == ProjectKind.Category ? "▸ " : "") + x.Name, "project", x.Id.ToString("N")) }).ToList();
        Pages(rows, "projects", page, list.Count > 8);
        await SendTextAsync("Проекты: выберите каталог", new(rows), ct);
    }

    public async Task ProjectAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var p = await db.Projects.SingleAsync(x => x.Id == id, ct);
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        nav.SelectedProjectId = id;
        await navigation.SaveNavigationAsync(nav, ct);
        var sessions = await db.Sessions.Where(x => x.ProjectId == id && !x.Hidden).OrderBy(x => x.Title).Take(12).ToListAsync(ct);
        var children = await db.Projects.Where(x => x.ParentProjectId == id).OrderBy(x => x.Name).Take(8).ToListAsync(ct);
        var rows = children.Select(x => new[] { B("▸ " + x.Name, "project", x.Id.ToString("N")) }).Concat(sessions.Select(x => new[] { B(x.Title, "session", x.Id.ToString("N")) })).ToList();
        rows.Add([B("Создать сессию", "newsession", id.ToString("N")), B("Все сессии", "sessions", "0")]);
        rows.Add([B("Проекты", "projects", "0")]);
        await SendTextAsync(p.Name + "\n" + p.AbsolutePath + "\n" + p.Technology, new(rows), ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string arg, int messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "projects":
                await ProjectsAsync(Page(arg), ct);
                return true;
            case "project":
                if (!Guid.TryParseExact(arg, "N", out var projectId))
                    return true;
                await ProjectAsync(projectId, ct);
                return true;
        }

        return false;
    }
}
