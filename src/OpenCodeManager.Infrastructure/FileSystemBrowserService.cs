using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
namespace OpenCodeManager.Infrastructure;

public sealed class FileSystemBrowserService(IPathPolicy paths, ManagerService manager, DirectoryNavigation navigation, IManagerDbFactory factory, OwnerGuard guard) : IFileSystemBrowser
{
    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return Task.FromResult(Directory.Exists(paths.DemandAllowed(path))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return Task.FromResult(false); }
    }
    public Task<DirectoryListing> GetDirectoriesAsync(string path, int page = 0, bool showHidden = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (page is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(page));
        var canonical = paths.DemandAllowed(path);
        if (!Directory.Exists(canonical)) return Task.FromResult(new DirectoryListing(canonical, null, [], page, false, "Каталог удалён или переименован"));
        string? parent = Directory.GetParent(canonical)?.FullName;
        try { if (parent is not null) parent = paths.DemandAllowed(parent); } catch (UnauthorizedAccessException) { parent = null; }
        try
        {
            // Keep only the first page-size * (page + 1) names, never a recursive tree.
            var selected = new SortedSet<string>(StringComparer.Ordinal);
            int capacity = checked((page + 1) * 8 + 1);
            foreach (var child in Directory.EnumerateDirectories(canonical))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!showHidden && Path.GetFileName(child).StartsWith('.')) continue;
                try { paths.DemandAllowed(child); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                selected.Add(child); if (selected.Count > capacity) selected.Remove(selected.Max!);
            }
            var entries = new List<DirectoryEntry>();
            foreach (var child in selected.Skip(page * 8).Take(8))
            {
                try { entries.Add(new(Path.GetFileName(child), paths.DemandAllowed(child), new DirectoryInfo(child).LinkTarget is not null)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return Task.FromResult(new DirectoryListing(canonical, parent, entries, page, selected.Count > (page + 1) * 8));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        { return Task.FromResult(new DirectoryListing(canonical, parent, [], page, false, "Нет доступа к каталогу или он изменился")); }
    }
    public async Task<DirectoryDetails> OpenAsync(string path, long owner, CancellationToken ct)
    {
        guard.Demand(owner); var canonical = paths.DemandAllowed(path);
        if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException();
        var project = await manager.AddProjectAsync(canonical, owner, ct);
        await manager.SyncDirectoryAsync(canonical, ct);
        await navigation.VisitAsync(canonical, owner, ct);
        await using var db = await factory.CreateAsync(ct); int sessions = await db.Sessions.CountAsync(x => x.ProjectId == project.Id && !x.Hidden, ct);
        return new(canonical, DetectTechnology(canonical), IsGit(canonical), project.Id, sessions);
    }
    private static bool IsGit(string directory)
    {
        for (var d = new DirectoryInfo(directory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git"))) return true;
        return false;
    }
    public static string DetectTechnology(string path)
    {
        var files = Directory.EnumerateFiles(path).Select(Path.GetFileName).ToArray(); var tags = new List<string>();
        if (files.Any(x => x!.EndsWith(".csproj") || x.EndsWith(".slnx") || x.EndsWith(".sln")))
        {
            bool web = false;
            foreach (var project in Directory.EnumerateFiles(path, "*.csproj"))
            {
                if (new FileInfo(project).Length > 1024 * 1024) continue;
                try { using var xml = XmlReader.Create(project, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 }); while (xml.Read()) if (xml.NodeType == XmlNodeType.Element && xml.Name == "Project" && xml.GetAttribute("Sdk") == "Microsoft.NET.Sdk.Web") web = true; } catch (XmlException) { }
            }
            tags.Add(web ? "ASP.NET Core" : ".NET");
        }
        if (File.Exists(Path.Combine(path, "config/application.rb"))) tags.Add("Rails"); else if (files.Contains("Gemfile")) tags.Add("Ruby");
        if (files.Contains("Cargo.toml")) tags.Add("Rust"); if (files.Contains("pyproject.toml")) tags.Add("Python");
        if (files.Contains("CMakeLists.txt")) tags.Add("C/C++"); if (files.Contains("package.json")) tags.Add("JavaScript/TypeScript");
        return tags.Count > 0 ? string.Join(", ", tags) : "Unknown";
    }
    public async Task<string> GitStatusAsync(string path, CancellationToken ct)
    {
        var canonical = paths.DemandAllowed(path); if (!IsGit(canonical)) return "Git: отсутствует";
        var start = new ProcessStartInfo("/usr/bin/git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "--no-optional-locks", "-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null", "-c", "core.pager=cat", "-C", canonical, "status", "--porcelain=v1", "--branch", "--untracked-files=normal" }) start.ArgumentList.Add(arg);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1"; start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null"; start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = Process.Start(start) ?? throw new IOException("git did not start");
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var output = new System.Text.StringBuilder(); var buffer = new char[4096];
            while (await process.StandardOutput.ReadAsync(buffer, timeout.Token) is var read && read > 0) { output.Append(buffer,0,read); if (output.Length > 100_000) throw new IOException("Git status exceeds display limit"); }
            await process.WaitForExitAsync(timeout.Token); await errorTask;
            return process.ExitCode == 0 ? output.ToString() : "Git status завершился с ошибкой (" + process.ExitCode + ")";
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
