using System.Text;
using System.Text.Json;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using Telegram.Bot.Types.ReplyMarkups;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class FileMenuHandler(TelegramUi ui, NavigationService navigation, IFileManager fileManager, DirectoryBrowserHandler browserScreens, FileTransferService transfers) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "fattach",
        "fattachsel",
        "fclear",
        "fdownload",
        "file",
        "flist",
        "fmkdir",
        "fpreview",
        "fsearch",
        "fselect",
        "fselected",
        "fzipdir",
        "fzipsel"
    };

    public static List<string> Selected(NavigationState nav) => JsonSerializer.Deserialize<List<string>>(nav.SelectedFilesJson ?? "[]") ?? [];
    public async Task<bool> HandleFileInputAsync(NavigationState nav, string input, CancellationToken ct)
    {
        if (nav.InputMode == "mkdir" && nav.BrowserPath is not null)
        {
            await fileManager.CreateDirectoryAsync(nav.BrowserPath, input, security.OwnerUserId, ct);
            nav.InputMode = null;
            await navigation.SaveNavigationAsync(nav, ct);
            await browserScreens.BrowseAsync(nav.BrowserPath, 0, nav.BrowserMessageId, ct);
            return true;
        }

        if (nav.InputMode == "search" && nav.BrowserPath is not null)
        {
            nav.InputMode = null;
            await navigation.SaveNavigationAsync(nav, ct);
            await FileListAsync(0, input, nav.BrowserMessageId, ct);
            return true;
        }

        return false;
    }

    public async Task FileListAsync(int page, string? search, int? messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        if (nav.BrowserPath is null)
        {
            await browserScreens.BrowserRootsAsync(messageId, ct);
            return;
        }

        if (search is not null)
        {
            nav.FileSearchQuery = search;
            await navigation.SaveNavigationAsync(nav, ct);
        }

        var listing = await fileManager.ListAsync(nav.BrowserPath, page, nav.FileSearchQuery, security.OwnerUserId, ct);
        var selected = Selected(nav);
        var rows = new List<InlineKeyboardButton[]>();
        foreach (var f in listing.Files)
        {
            var key = StoreChoice("file", f.CanonicalPath, null, null);
            rows.Add([B(f.Name, "file", key), B(selected.Contains(f.CanonicalPath) ? "[x]" : "[ ]", "fselect", key)]);
        }

        if (page > 0)
            rows.Add([B("←", "flist", (page - 1).ToString())]);
        if (listing.HasMore)
            rows.Add([B("→", "flist", (page + 1).ToString())]);
        rows.Add([B("Поиск", "fsearch", "0"), B("Скачать папку ZIP", "fzipdir", "0")]);
        if (selected.Count > 0)
        {
            rows.Add([B("Скачать выбранные", "fselected", "0"), B("ZIP выбранных", "fzipsel", "0")]);
            rows.Add([B("Добавить к задаче", "fattachsel", "0"), B("Очистить", "fclear", "0")]);
        }

        rows.Add([B("Папки", "fpage", "0"), B("Корни", "files", "0")]);
        await RenderBrowserAsync(nav.BrowserPath + "\nФайлы. Выбрано: " + selected.Count + "\nСекреты исключаются политикой передачи.", new(rows), messageId, ct);
    }

    public async Task FileCardAsync(string path, int? messageId, CancellationToken ct)
    {
        var f = await fileManager.InspectAsync(path, security.OwnerUserId, ct);
        var key = StoreChoice("file", f.CanonicalPath, null, null);
        await RenderBrowserAsync($"{f.Name}\nРазмер: {f.Size:N0} bytes\nТип: {f.MimeType}\n{f.Width} × {f.Height}", new(new[] { new[] { B("Скачать оригинал", "fdownload", key), B("Предпросмотр", "fpreview", key) }, new[] { B("Отправить агенту", "fattach", key), B("Выбрать", "fselect", key) }, new[] { B("Назад", "flist", "0") } }), messageId, ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string argument, int messageId, CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "flist":
                await FileListAsync(Page(argument), null, messageId, ct);
                return true;
            case "fsearch":
                nav.InputMode = "search";
                await navigation.SaveNavigationAsync(nav, ct);
                await SendTextAsync("Введите часть имени файла. Поиск в текущей папке, без рекурсивного обхода.", null, ct);
                return true;
            case "fmkdir":
                nav.InputMode = "mkdir";
                await navigation.SaveNavigationAsync(nav, ct);
                await SendTextAsync("Введите имя новой папки (один компонент пути).", null, ct);
                return true;
            case "file":
            case "fdownload":
            case "fpreview":
            case "fattach":
            case "fselect":
                if (!choices.TryGetValue(argument, out var choice) || choice.Kind != "file" || choice.Expires < DateTime.UtcNow)
                {
                    await SendTextAsync("Выбор устарел. Обновите список файлов.", null, ct);
                    return true;
                }

                if (action == "file")
                    await FileCardAsync(choice.Value, messageId, ct);
                if (action == "fdownload")
                    await transfers.DownloadProjectFileAsync(choice.Value, false, ct);
                if (action == "fpreview")
                {
                    var info = await fileManager.InspectAsync(choice.Value, security.OwnerUserId, ct);
                    if (info.MimeType.StartsWith("image/", StringComparison.Ordinal))
                        await transfers.DownloadProjectFileAsync(choice.Value, true, ct);
                    else
                    {
                        await using var stream = await fileManager.OpenDownloadAsync(choice.Value, security.OwnerUserId, ct);
                        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
                        var buffer = new char[100_000];
                        int read = await reader.ReadAsync(buffer, ct);
                        var text = new string (buffer, 0, read);
                        if (text.Contains('\0'))
                            throw new InvalidDataException("Binary preview unavailable");
                        await SendTextAsync(text, null, ct);
                    }
                }

                if (action == "fattach")
                {
                    var file = await fileManager.ImportProjectFileAsync(choice.Value, security.OwnerUserId, ct);
                    await fileManager.AddToNextTaskAsync([file.Id], security.OwnerUserId, ct);
                    await SendTextAsync("Файл добавлен к следующей задаче. Выберите модель с image input для изображений.", Menu, ct);
                }

                if (action == "fselect")
                {
                    var selected = Selected(nav);
                    if (!selected.Remove(choice.Value))
                        selected.Add(choice.Value);
                    if (selected.Count > 20)
                        throw new InvalidDataException("Maximum 20 selected files");
                    nav.SelectedFilesJson = JsonSerializer.Serialize(selected);
                    await navigation.SaveNavigationAsync(nav, ct);
                    await FileListAsync(0, null, messageId, ct);
                }

                return true;
            case "fclear":
                nav.SelectedFilesJson = null;
                await navigation.SaveNavigationAsync(nav, ct);
                await FileListAsync(0, null, messageId, ct);
                return true;
            case "fselected":
                foreach (var file in Selected(nav))
                    await transfers.DownloadProjectFileAsync(file, false, ct);
                return true;
            case "fattachsel":
                foreach (var path in Selected(nav))
                {
                    var file = await fileManager.ImportProjectFileAsync(path, security.OwnerUserId, ct);
                    await fileManager.AddToNextTaskAsync([file.Id], security.OwnerUserId, ct);
                }

                await SendTextAsync("Выбранные файлы добавлены к следующей задаче.", Menu, ct);
                return true;
            case "fzipdir":
            case "fzipsel":
                var archive = await fileManager.CreateArchiveAsync(action == "fzipdir" ? nav.BrowserPath : null, action == "fzipsel" ? Selected(nav) : null, security.OwnerUserId, ct);
                try
                {
                    await transfers.SendStoredFileAsync(archive, "files.zip", "application/zip", false, null, ct);
                }
                finally
                {
                    System.IO.File.Delete(archive);
                }

                return true;
        }

        return false;
    }
}
