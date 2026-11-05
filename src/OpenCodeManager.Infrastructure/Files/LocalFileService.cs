using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class LocalFileService(IPathPolicy paths, OwnerGuard guard, FileStorageOptions options, FileAccessPolicy access)
{
    public async Task<FileListing> ListAsync(string directory, int page, string? search, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        var dir = paths.DemandAllowed(directory);
        if (page is < 0 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(page));
        var top = new SortedSet<string>(StringComparer.Ordinal);
        int capacity = (page + 1) * 8 + 1;
        foreach (var path in Directory.EnumerateFiles(dir))
        {
            ct.ThrowIfCancellationRequested();
            if (search is { Length: > 0 } && !Path.GetFileName(path).Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                access.ReadPath(path, owner);
            }
            catch (Exception e)when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            top.Add(path);
            if (top.Count > capacity)
                top.Remove(top.Max!);
        }

        var files = new List<LocalFileInfo>();
        foreach (var path in top.Skip(page * 8).Take(8))
        {
            try
            {
                files.Add(await InspectAsync(path, owner, ct));
            }
            catch (Exception e)when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }
        }

        return new(files, page, top.Count > (page + 1) * 8);
    }

    public async Task<LocalFileInfo> InspectAsync(string path, long owner, CancellationToken ct)
    {
        var canonical = access.ReadPath(path, owner);
        var info = new FileInfo(canonical);
        if (info.Length > options.MaxDownloadBytes)
            throw new InvalidDataException("File exceeds Telegram transfer limit");
        await using var stream = new FileStream(canonical, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[1024 * 1024];
        int size = await stream.ReadAsync(header, ct);
        var(mime, width, height) = ImageHeaderReader.Read(header.AsSpan(0, size));
        if (width is int w && height is int h && ((long)w * h > options.MaxImagePixels || w <= 0 || h <= 0))
            throw new InvalidDataException("Image exceeds pixel limit");
        return new(info.Name, canonical, info.Length, mime, width, height);
    }

    public async Task<Stream> OpenDownloadAsync(string path, long owner, CancellationToken ct)
    {
        var info = await InspectAsync(path, owner, ct);
        var stream = new FileStream(access.ReadPath(info.CanonicalPath, owner), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        try
        {
            await access.CheckContentAsync(stream, ct);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public Task CreateDirectoryAsync(string parent, string name, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        ct.ThrowIfCancellationRequested();
        var dir = paths.DemandAllowed(parent);
        var target = paths.DemandAllowed(Path.Combine(dir, FileAccessPolicy.ValidateName(name)));
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException("Already exists");
        Directory.CreateDirectory(target);
        return Task.CompletedTask;
    }
}
