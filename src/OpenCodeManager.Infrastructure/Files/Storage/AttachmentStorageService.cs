using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class AttachmentStorageService(IManagerDbFactory factory, IPathPolicy paths, OwnerGuard guard, FileStorageOptions options, TelegramOptions telegram, FileAccessPolicy access, FileStoragePaths storage, LocalFileService files)
{
    public string NewUploadPath(string name, long owner)
    {
        guard.Demand(owner);
        FileAccessPolicy.ValidateName(name);
        return Path.Combine(storage.Get("uploads"), Guid.NewGuid().ToString("N") + Path.GetExtension(name));
    }

    public async Task<FileAttachment> RegisterUploadAsync(string tempPath, string originalName, string? telegramFileId, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        FileAccessPolicy.ValidateName(originalName);
        var path = access.StoredPath(tempPath);
        var info = new FileInfo(path);
        if (info.Length > telegram.MaxAttachmentBytes)
            throw new InvalidDataException("Upload too large");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await access.CheckContentAsync(stream, ct);
        var header = new byte[1024 * 1024];
        int read = await stream.ReadAsync(header, ct);
        var(mime, width, height) = ImageHeaderReader.Read(header.AsSpan(0, read));
        if (header.AsSpan(0, read).StartsWith(new byte[] { 0x7f, 69, 76, 70 }) || header.AsSpan(0, read).StartsWith("MZ"u8))
            throw new InvalidDataException("Executable binary upload rejected");
        var ext = Path.GetExtension(originalName).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp")
        {
            if (!mime.StartsWith("image/", StringComparison.Ordinal) || width is null || height is null || (long)width * height > options.MaxImagePixels)
                throw new InvalidDataException("Invalid or oversized image");
        }

        if (ext == ".pdf" && mime != "application/pdf")
            throw new InvalidDataException("Invalid PDF");
        if (ext is ".zip" or ".docx")
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            if (archive.Entries.Count > options.MaxArchiveFiles || archive.Entries.Sum(x => x.Length) > options.MaxArchiveBytes)
                throw new InvalidDataException("Archive exceeds limits");
            if (ext == ".docx" && archive.GetEntry("word/document.xml")is null)
                throw new InvalidDataException("Invalid DOCX");
            mime = ext == ".docx" ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : "application/zip";
        }

        if (mime == "application/octet-stream" && read > 0 && !header.AsSpan(0, read).Contains((byte)0))
            mime = "text/plain";
        var file = new FileAttachment
        {
            OriginalName = originalName,
            LocalPath = path,
            MimeType = mime,
            Size = info.Length,
            TelegramFileId = telegramFileId,
            Width = width,
            Height = height
        };
        await using var db = await factory.CreateAsync(ct);
        db.Attachments.Add(file);
        await db.SaveChangesAsync(ct);
        return file;
    }

    public async Task SaveUploadAsync(Guid attachmentId, string directory, bool overwrite, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await using var db = await factory.CreateAsync(ct);
        var f = await db.Attachments.SingleAsync(x => x.Id == attachmentId, ct);
        var source = access.StoredPath(f.LocalPath);
        var dir = paths.DemandAllowed(directory);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException();
        var target = paths.DemandAllowed(Path.Combine(dir, FileAccessPolicy.ValidateName(f.OriginalName)));
        if (FileAccessPolicy.IsSecretName(target))
            throw new UnauthorizedAccessException();
        var stage = Path.Combine(dir, ".manager-upload-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await access.CheckContentAsync(input, ct);
                await using var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
            }

            paths.DemandAllowed(dir);
            paths.DemandAllowed(target);
            File.Move(stage, target, overwrite);
            db.Transfers.Add(new() { AttachmentId = f.Id, Direction = "SaveToProject", LocalPath = target, Status = "Completed", CompletedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            if (File.Exists(stage))
                File.Delete(stage);
        }
    }

    public async Task DeleteUploadAsync(Guid attachmentId, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await using var db = await factory.CreateAsync(ct);
        var f = await db.Attachments.SingleAsync(x => x.Id == attachmentId, ct);
        if (await db.TaskFiles.AnyAsync(x => x.AttachmentId == attachmentId, ct))
            throw new InvalidOperationException("Attachment is referenced by a task");
        File.Delete(access.StoredPath(f.LocalPath));
        db.Attachments.Remove(f);
        await db.SaveChangesAsync(ct);
    }

    public async Task<FileAttachment> ImportProjectFileAsync(string path, long owner, CancellationToken ct)
    {
        var info = await files.InspectAsync(path, owner, ct);
        if (info.Size > telegram.MaxAttachmentBytes)
            throw new InvalidDataException("Attachment too large");
        var target = NewUploadPath(info.Name, owner);
        try
        {
            await using (var input = await files.OpenDownloadAsync(path, owner, ct))
            {
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }

            return await RegisterUploadAsync(target, info.Name, null, owner, ct);
        }
        catch
        {
            File.Delete(target);
            throw;
        }
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var referenced = await db.TaskFiles.Select(x => x.AttachmentId).ToListAsync(ct);
        foreach (var f in await db.Attachments.Where(x => x.ExpiresAtUtc < DateTime.UtcNow && !referenced.Contains(x.Id)).ToListAsync(ct))
        {
            File.Delete(access.StoredPath(f.LocalPath));
            db.Attachments.Remove(f);
        }

        await db.Transfers.Where(x => x.Status == "Pending" || x.Status == "Running").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "NeedsReconciliation").SetProperty(x => x.Error, "Restart interrupted transfer"), ct);
        await db.SaveChangesAsync(ct);
        foreach (var path in Directory.EnumerateFiles(storage.Get("downloads")))
            if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-1))
                File.Delete(path);
    }
}
