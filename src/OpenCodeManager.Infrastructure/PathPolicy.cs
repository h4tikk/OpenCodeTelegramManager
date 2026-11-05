using OpenCodeManager.Application;
using OpenCodeManager.Domain;
namespace OpenCodeManager.Infrastructure;

public sealed class PathPolicy(ProjectOptions options) : IPathPolicy
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", ".ssh", ".gnupg", ".aws", ".codex", ".agents", "node_modules", "vendor", "bin", "obj", "target", ".venv", "venv", ".bundle", ".idea", ".vscode", ".local", ".config", "secrets" };
    public string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (item.LinkTarget is not null) current = (item.ResolveLinkTarget(true) ?? throw new IOException("Broken symbolic link")).FullName;
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }
    public string DemandAllowed(string path)
    {
        var actual = Canonicalize(path);
        var allowed = options.AllowedRoots.Where(Directory.Exists).Select(Canonicalize).Any(root => actual == root || actual.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        if (!allowed || actual.Split('/').Any(x => Excluded.Contains(x))) throw new UnauthorizedAccessException("Directory is outside allowed project roots");
        return actual;
    }
    public string LockKey(string path)
    {
        var canonical = DemandAllowed(path);
        for (var dir = new DirectoryInfo(canonical); dir is not null; dir = dir.Parent)
        {
            var marker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(marker)) return Canonicalize(marker);
            if (File.Exists(marker))
            {
                var line = File.ReadLines(marker).FirstOrDefault();
                if (line?.StartsWith("gitdir: ", StringComparison.Ordinal) == true)
                {
                    var gitDir = Canonicalize(Path.GetFullPath(line[8..], dir.FullName));
                    var common = Path.Combine(gitDir, "commondir");
                    return File.Exists(common) ? Canonicalize(Path.GetFullPath(File.ReadAllText(common).Trim(), gitDir)) : gitDir;
                }
            }
        }
        return canonical;
    }
    public IEnumerable<(string Path, string? Parent, ProjectKind Kind)> Discover()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in options.AllowedRoots.Where(Directory.Exists))
            foreach (var child in Directory.EnumerateDirectories(root).Order())
            {
                string actual;
                try { actual = DemandAllowed(child); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                if (!Path.GetFileName(actual).StartsWith('.') && seen.Add(actual)) yield return (actual, null, ProjectKind.Category);
            }
    }
}
