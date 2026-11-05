using OpenCodeManager.Domain;
using Telegram.Bot.Types.ReplyMarkups;

namespace OpenCodeManager.Telegram;
public static class TelegramKeyboards
{
    public static InlineKeyboardButton B(string label, string action, string argument) => InlineKeyboardButton.WithCallbackData(label, action + ":" + argument);
    public static InlineKeyboardMarkup Menu => new(new[] { new[] { B("Проекты", "projects", "0"), B("Сессии", "sessions", "0") }, new[] { B("Новая задача", "newtask", "0"), B("Мои задачи", "tasks", "0") }, new[] { B("Модели", "models", "0"), B("Агенты", "agents", "0") }, new[] { B("Файловый менеджер", "files", "0") }, new[] { B("Настройки", "settings", "0"), B("Обновить", "home", "0") } });

    public static int Page(string arg) => int.TryParse(arg, out var page) && page is >= 0 and <= 10000 ? page : 0;
    public static void Pages(List<InlineKeyboardButton[]> rows, string action, int page, bool more)
    {
        var buttons = new List<InlineKeyboardButton>();
        if (page > 0)
            buttons.Add(B("←", action, (page - 1).ToString()));
        if (more)
            buttons.Add(B("→", action, (page + 1).ToString()));
        if (buttons.Count > 0)
            rows.Add(buttons.ToArray());
        rows.Add([B("Меню", "home", "0")]);
    }

    public static InlineKeyboardMarkup TaskButtons(AgentTask t) => new(new[] { new[] { B("Логи", "logs", t.Id.ToString("N")), B("Diff", "diff", t.Id.ToString("N")) }, new[] { B("Обновить", "task", t.Id.ToString("N")), B("Остановить", "stop", t.Id.ToString("N")) }, new[] { B("Полный результат", "result", t.Id.ToString("N")) }, new[] { B("Файлы результата", "resultfiles", t.Id.ToString("N")), B("Продолжить", "sessiontask", t.Id.ToString("N")) }, new[] { B("Меню", "home", "0") } });
}
