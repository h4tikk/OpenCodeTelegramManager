using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using Telegram.Bot;
using Telegram.Bot.Types;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class UploadHandler(TelegramUi ui, IManagerDbFactory factory, NavigationService navigation, IFileManager fileManager, FileTransferService transfers, TaskMenuHandler taskScreens) : TelegramHandler(ui), ITelegramCallbackHandler
{
    public IReadOnlySet<string> Actions { get; } = new HashSet<string>
    {
        "confirmwrite",
        "uattach",
        "udelete",
        "udownload",
        "uoverwrite",
        "usave"
    };

    public async Task ReceiveFileAsync(Message msg, int updateId, CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        string fileId, name;
        long size;
        if (msg.Document is { } document)
        {
            fileId = document.FileId;
            name = document.FileName ?? "document.bin";
            size = document.FileSize ?? 0;
        }
        else
        {
            var photo = msg.Photo!.OrderByDescending(x => x.Width * x.Height).First();
            fileId = photo.FileId;
            name = "telegram-photo-" + updateId + ".jpg";
            size = photo.FileSize ?? 0;
        }

        if (size > options.MaxAttachmentBytes)
            throw new InvalidDataException("Attachment exceeds upload limit");
        var path = fileManager.NewUploadPath(name, security.OwnerUserId);
        var transfer = await transfers.StartTransferAsync("Upload", null, path, ct);
        try
        {
            var file = await bot.GetFile(fileId, ct);
            if (file.FileSize > options.MaxAttachmentBytes)
                throw new InvalidDataException("Attachment exceeds upload limit");
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                using var limited = new LimitedWriteStream(output, options.MaxAttachmentBytes);
                await bot.DownloadFile(file.FilePath!, limited, ct);
            }

            var attachment = await fileManager.RegisterUploadAsync(path, name, fileId, security.OwnerUserId, ct);
            await transfers.FinishTransferAsync(transfer.Id, null, ct, attachment.Id);
            if (!string.IsNullOrWhiteSpace(msg.Caption))
            {
                await fileManager.AddToNextTaskAsync([attachment.Id], security.OwnerUserId, ct);
                var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
                await taskScreens.SubmitAsync(nav, msg.Caption, updateId, ct);
            }
            else
                await UploadCardAsync(attachment.Id, ct);
        }
        catch (Exception e)
        {
            File.Delete(path);
            await transfers.FinishTransferAsync(transfer.Id, e.GetType().Name, CancellationToken.None);
            throw;
        }
    }

    public async Task UploadCardAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var file = await db.Attachments.SingleAsync(x => x.Id == id, ct);
        await SendTextAsync($"Получен файл: {file.OriginalName}\n{file.Size:N0} bytes\n{file.MimeType}\n{file.Width} × {file.Height}\nСохранён во временном хранилище.", new(new[] { new[] { B("Добавить к задаче", "uattach", id.ToString("N")), B("Сохранить в проект", "usave", id.ToString("N")) }, new[] { B("Выбрать папку", "files", "0"), B("Удалить временный", "udelete", id.ToString("N")) }, new[] { B("Оригинал", "udownload", id.ToString("N")), B("Меню", "home", "0") } }), ct);
    }

    public async Task<bool> HandleCallbackAsync(string action, string argument, int messageId, CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        switch (action)
        {
            case "uattach":
            case "usave":
            case "uoverwrite":
            case "udelete":
            case "udownload":
                if (!Guid.TryParseExact(argument, "N", out var uploadId))
                    return true;
                if (action == "uattach")
                {
                    await fileManager.AddToNextTaskAsync([uploadId], security.OwnerUserId, ct);
                    await SendTextAsync("Вложение добавлено. Напишите задачу в выбранной сессии.", Menu, ct);
                }

                if (action == "usave" || action == "uoverwrite")
                {
                    if (nav.BrowserPath is null)
                    {
                        await SendTextAsync("Выберите папку в файловом менеджере, затем снова нажмите «Сохранить в проект» у вложения.", Menu, ct);
                        return true;
                    }

                    if (action == "usave")
                    {
                        await using var db = await factory.CreateAsync(ct);
                        var f = await db.Attachments.SingleAsync(x => x.Id == uploadId, ct);
                        if (System.IO.File.Exists(Path.Combine(nav.BrowserPath, f.OriginalName)))
                        {
                            var key = StoreChoice("overwrite", nav.BrowserPath, uploadId.ToString("N"), null);
                            await SendTextAsync("Файл уже существует в выбранной папке. Подтвердите перезапись: " + nav.BrowserPath + "/" + f.OriginalName, new(new[] { new[] { B("Перезаписать", "confirmwrite", key), B("Отмена", "home", "0") } }), ct);
                            return true;
                        }
                    }

                    // Direct uoverwrite callbacks are deliberately rejected; overwrite requires the one-use scoped confirmation.
                    if (action == "uoverwrite")
                        return true;
                    await fileManager.SaveUploadAsync(uploadId, nav.BrowserPath, false, security.OwnerUserId, ct);
                    await SendTextAsync("Файл сохранён в выбранной папке.", Menu, ct);
                }

                if (action == "udelete")
                {
                    await fileManager.DeleteUploadAsync(uploadId, security.OwnerUserId, ct);
                    await SendTextAsync("Временный файл удалён.", Menu, ct);
                }

                if (action == "udownload")
                {
                    await using var db = await factory.CreateAsync(ct);
                    var f = await db.Attachments.SingleAsync(x => x.Id == uploadId, ct);
                    await transfers.SendStoredFileAsync(f.LocalPath, f.OriginalName, f.MimeType, false, f.Id, ct);
                }

                return true;
            case "confirmwrite":
                if (!choices.TryRemove(argument, out var confirmation) || confirmation.Kind != "overwrite" || confirmation.Expires < DateTime.UtcNow || !Guid.TryParseExact(confirmation.Provider, "N", out var confirmedId))
                    return true;
                await fileManager.SaveUploadAsync(confirmedId, confirmation.Value, true, security.OwnerUserId, ct);
                await SendTextAsync("Файл заменён после подтверждения.", Menu, ct);
                return true;
        }

        return false;
    }
}
