using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class CatalogMenuHandler(TelegramUi ui, IManagerDbFactory factory, IOpenCodeClient api, NavigationService navigation, ModelManager models, AgentManager agents) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "agents",
        "choose",
        "models"
    };

    public async Task ModelsAsync(int page, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        await using var db = await factory.CreateAsync(ct);
        var directory = nav.SelectedSessionId is Guid id ? (await db.Sessions.SingleAsync(x => x.Id == id, ct)).WorkingDirectory : null;
        var all = (await api.GetModelsAsync(directory, ct)).OrderBy(x => x.ProviderId).ThenBy(x => x.Name).ToList();
        var list = all.Skip(page * 8).Take(9).ToList();
        var rows = list.Take(8).Select(x => new[] { B((x.Enabled ? "" : "✕ ") + x.ProviderId + "/" + x.Name, "choose", StoreChoice(nav.InputMode == "prompt" ? "taskmodel" : "model", x.Id, x.ProviderId, nav.SelectedSessionId)) }).ToList();
        Pages(rows, "models", page, list.Count > 8);
        await SendTextAsync("Модели (каталог; генерация не проверялась). Выбор для " + (nav.SelectedSessionId is null ? "defaults" : "сессии") + ". Провайдеры: " + string.Join(", ", all.Select(x => x.ProviderId).Distinct()), new(rows), ct);
    }

    public async Task AgentsAsync(int page, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        await using var db = await factory.CreateAsync(ct);
        var directory = nav.SelectedSessionId is Guid id ? (await db.Sessions.SingleAsync(x => x.Id == id, ct)).WorkingDirectory : null;
        var list = (await api.GetAgentsAsync(directory, ct)).Where(x => !x.Hidden && x.Mode != "subagent").Skip(page * 8).Take(9).ToList();
        var rows = list.Take(8).Select(x => new[] { B(x.Name, "choose", StoreChoice(nav.InputMode == "prompt" ? "taskagent" : "agent", x.Id, null, nav.SelectedSessionId)) }).ToList();
        Pages(rows, "agents", page, list.Count > 8);
        await SendTextAsync("Агенты для " + (nav.SelectedSessionId is null ? "defaults" : "сессии"), new(rows), ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string arg, int messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "models":
                await ModelsAsync(Page(arg), ct);
                return true;
            case "agents":
                await AgentsAsync(Page(arg), ct);
                return true;
            case "choose":
                if (!choices.TryRemove(arg, out var choice) || choice.Expires < DateTime.UtcNow)
                {
                    await SendTextAsync("Кнопка устарела. Обновите список.", Menu, ct);
                    return true;
                }

                if(choice.Kind is not ("model" or "taskmodel" or "agent" or "taskagent")) return true;
                if (choice.Kind is "model" or "taskmodel")
                    await models.SelectModelAsync(choice.SessionId, new(choice.Value, choice.Provider!), security.OwnerUserId, ct, choice.Kind == "taskmodel");
                else
                    await agents.SelectAgentAsync(choice.SessionId, choice.Value, security.OwnerUserId, ct, choice.Kind == "taskagent");
                await SendTextAsync("Выбор сохранён для следующих задач.", Menu, ct);
                return true;
        }

        return false;
    }
}
