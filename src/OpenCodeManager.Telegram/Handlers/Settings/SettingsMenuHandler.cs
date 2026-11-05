using OpenCodeManager.Application;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class SettingsMenuHandler(TelegramUi ui, NavigationService navigation, SessionManager sessions, PermissionManager permissions, AutonomyOptions autonomy) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "addproject",
        "approve",
        "defaults",
        "reject",
        "rule",
        "settings",
        "sync"
    };

    public async Task<bool> HandleCallbackAsync(string action, string arg, int messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "approve":
            case "reject":
            case "rule":
                if (Guid.TryParseExact(arg, "N", out var permissionId))
                {
                    await permissions.ReplyAsync(permissionId, action != "reject", action == "rule", security.OwnerUserId, ct);
                    await SendTextAsync("Ответ на разрешение отправлен.", Menu, ct);
                }

                return true;
            case "sync":
                await sessions.SyncAsync(true, ct);
                await SendTextAsync("Проекты и сессии синхронизированы.", Menu, ct);
                return true;
            case "addproject":
                nav.InputMode = "addproject";
                await navigation.SaveNavigationAsync(nav, ct);
                await SendTextAsync("Введите существующий путь внутри разрешённых корней.", null, ct);
                return true;
            case "defaults":
                nav.SelectedSessionId = null;
                await navigation.SaveNavigationAsync(nav, ct);
                await SendTextAsync("Теперь выбор модели и агента меняет значения по умолчанию.", Menu, ct);
                return true;
            case "settings":
                await SendTextAsync(autonomy.Enabled ? "Автономный режим: безопасные действия проекта разрешаются автоматически, опасные отклоняются с объяснением. Shell работает в изолированной копии без сети и секретов хоста; доступны локальные кэши зависимостей. Лимиты задаются в защищённой конфигурации. /cancel сбрасывает ввод." : "Ручной режим разрешений: безопасные файлы проекта подтверждаются автоматически, остальные действия требуют решения владельца. Лимиты задаются в защищённой конфигурации. /cancel сбрасывает ввод.", new(new[] { new[] { B("Синхронизация", "sync", "0"), B("Добавить каталог", "addproject", "0") }, new[] { B("Выбрать defaults", "defaults", "0"), B("Меню", "home", "0") } }), ct);
                return true;
        }

        return false;
    }
}
