using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class ArchiveService(LocalFileService localFiles, FileStoragePaths storage, IPathPolicy paths, OwnerGuard guard, FileStorageOptions options)
{
    public async Task<string> CreateArchiveAsync(string? directory, IReadOnlyList<string>? files, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.MaxArchiveSeconds));
        ct = timeout.Token;
        var baseDir = directory is null ? null : paths.DemandAllowed(directory);
        var outputPath = Path.Combine(storage.Get("downloads"), Guid.NewGuid() + ".zip");
        int count = 0;
        long bytes = 0;
        try
        {
            using (var archive = new ZipArchive(new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None), ZipArchiveMode.Create))
            {
                var pending = new Stack<(string Path, int Depth)>();
                int directoryCount = 0;
                if (baseDir is not null)
                    pending.Push((baseDir, 0));
                var candidates = new List<string>();
                if (files is not null)
                    candidates.AddRange(files);
                while (pending.TryPop(out var dir))
                {
                    ct.ThrowIfCancellationRequested();
                    if (dir.Depth > options.MaxArchiveDepth)
                        throw new InvalidDataException("Archive depth limit exceeded");
                    foreach (var file in Directory.EnumerateFiles(paths.DemandAllowed(dir.Path)))
                    {
                        if (new FileInfo(file).LinkTarget is null && !FileAccessPolicy.IsSecretName(file))
                        {
                            candidates.Add(file);
                            if (candidates.Count > options.MaxArchiveFiles)
                                throw new InvalidDataException("Archive file count limit exceeded");
                        }
                    }

                    foreach (var sub in Directory.EnumerateDirectories(dir.Path))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (++directoryCount > options.MaxArchiveFiles)
                            throw new InvalidDataException("Archive directory count limit exceeded");
                        if (new DirectoryInfo(sub).LinkTarget is not null || FileAccessPolicy.IsSecretName(sub) || Path.GetFileName(sub)is "node_modules" or "bin" or "obj" or "target" or ".venv")
                            continue;
                        try
                        {
                            pending.Push((paths.DemandAllowed(sub), dir.Depth + 1));
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var candidate in candidates.Distinct())
                {
                    ct.ThrowIfCancellationRequested();
                    LocalFileInfo info;
                    try
                    {
                        info = await localFiles.InspectAsync(candidate, owner, ct);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    bytes += info.Size;
                    if (bytes > options.MaxArchiveBytes || ++count > options.MaxArchiveFiles)
                        throw new InvalidDataException("Archive exceeds transfer limits");
                    string entryName = baseDir is not null ? Path.GetRelativePath(baseDir, info.CanonicalPath) : info.Name;
                    if (entryName.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(entryName))
                        throw new UnauthorizedAccessException();
                    if (!names.Add(entryName))
                        entryName = count + "-" + info.Name;
                    Stream input;
                    try
                    {
                        input = await localFiles.OpenDownloadAsync(info.CanonicalPath, owner, ct);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    await using (input)
                    {
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        await using var output = entry.Open();
                        await input.CopyToAsync(output, ct);
                    }
                }
            }

            if (new FileInfo(outputPath).Length > options.MaxDownloadBytes)
                throw new InvalidDataException("ZIP exceeds Telegram limit");
            return outputPath;
        }
        catch
        {
            File.Delete(outputPath);
            throw;
        }
    }
}
