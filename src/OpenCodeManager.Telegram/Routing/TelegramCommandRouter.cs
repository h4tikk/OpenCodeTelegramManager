using OpenCodeManager.Application;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class TelegramCommandRouter(TelegramUi ui, NavigationService navigation, MainMenuHandler home, ProjectMenuHandler projectScreens, SessionMenuHandler sessionScreens, TaskMenuHandler taskScreens, CatalogMenuHandler catalog, DirectoryBrowserHandler browserScreens) : TelegramHandler(ui)
{
    public async Task HandleAsync(string input, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        var command = input.Split(' ', 2)[0].Split('@')[0];
        switch (command)
        {
            case "/start":
            case "/status":
                await home.HomeAsync(ct);
                break;
            case "/help":
                await SendTextAsync("/start — меню\n/status — API, SQLite и очередь\n/projects — проекты\n/sessions — сессии\n/tasks — задачи\n/models — модели\n/agents — агенты\n/cancel — сброс ввода\n\nВыберите сессию → «Написать задачу». Текст вне режима ввода тоже продолжает выбранную сессию. Остановка запущенной задачи доступна в её карточке. Настройки модели и агента применяются к следующим задачам; каталог не подтверждает доступность генерации.", Menu, ct);
                break;
            case "/files":
                await browserScreens.BrowserRootsAsync(null, ct);
                break;
            case "/projects":
                await projectScreens.ProjectsAsync(0, ct);
                break;
            case "/sessions":
                await sessionScreens.SessionsAsync(0, ct);
                break;
            case "/tasks":
                await taskScreens.TasksAsync(0, ct);
                break;
            case "/models":
                await catalog.ModelsAsync(0, ct);
                break;
            case "/agents":
                await catalog.AgentsAsync(0, ct);
                break;
            case "/cancel":
                nav.InputMode = null;
                nav.DraftProviderId = null;
                nav.DraftModelId = null;
                nav.DraftAgent = null;
                nav.PendingAttachmentText = null;
                nav.PendingFileIdsJson = null;
                await navigation.SaveNavigationAsync(nav, ct);
                await SendTextAsync("Ввод сброшен. Для остановки выполнения откройте задачу.", Menu, ct);
                break;
            default:
                await SendTextAsync("Неизвестная команда. /help", Menu, ct);
                break;
        }
    }
}
