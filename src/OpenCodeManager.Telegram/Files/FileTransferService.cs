using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Telegram;
public sealed class FileTransferService(TelegramUi ui, IManagerDbFactory factory, IFileManager fileManager, TelegramSender sender) : TelegramHandler(ui)
{
    public async Task DownloadProjectFileAsync(string path, bool preview, CancellationToken ct)
    {
        var info = await fileManager.InspectAsync(path, security.OwnerUserId, ct);
        await using var stream = await fileManager.OpenDownloadAsync(path, security.OwnerUserId, ct);
        var transfer = await StartTransferAsync("Download", null, info.CanonicalPath, ct);
        try
        {
            await sender.SendStreamAsync(stream, info.Name, info.MimeType, preview, ct);
            await FinishTransferAsync(transfer.Id, null, ct);
        }
        catch (Exception e)
        {
            await FinishTransferAsync(transfer.Id, e.GetType().Name, CancellationToken.None);
            throw;
        }
    }

    public async Task SendStoredFileAsync(string path, string name, string mime, bool preview, Guid? attachment, CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        var transfer = await StartTransferAsync("Download", attachment, path, ct);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            await sender.SendStreamAsync(stream, name, mime, preview, ct);
            await FinishTransferAsync(transfer.Id, null, ct);
        }
        catch (Exception e)
        {
            await FinishTransferAsync(transfer.Id, e.GetType().Name, CancellationToken.None);
            throw;
        }
    }

    public async Task<FileTransfer> StartTransferAsync(string direction, Guid? attachment, string path, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var transfer = new FileTransfer
        {
            Direction = direction,
            AttachmentId = attachment,
            LocalPath = path,
            Status = "Running"
        };
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync(ct);
        return transfer;
    }

    public async Task FinishTransferAsync(Guid id, string? error, CancellationToken ct, Guid? attachment = null)
    {
        await using var db = await factory.CreateAsync(ct);
        var t = await db.Transfers.SingleAsync(x => x.Id == id, ct);
        t.Status = error is null ? "Completed" : "Failed";
        t.Error = error;
        t.AttachmentId ??= attachment;
        t.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
