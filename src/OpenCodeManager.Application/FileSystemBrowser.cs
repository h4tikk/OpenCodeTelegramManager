using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;
namespace OpenCodeManager.Application;

public sealed record DirectoryEntry(string Name, string CanonicalPath, bool IsSymbolicLink);
public sealed record DirectoryListing(string CanonicalPath, string? ParentPath, List<DirectoryEntry> Directories, int Page, bool HasMore, string? Error = null);
public sealed record DirectoryDetails(string CanonicalPath, string Technology, bool IsGitRepository, Guid ProjectId, int Sessions);
public interface IFileSystemBrowser
{
    Task<DirectoryListing> GetDirectoriesAsync(string path, int page = 0, bool showHidden = false, CancellationToken cancellationToken = default);
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<DirectoryDetails> OpenAsync(string path, long owner, CancellationToken ct);
    Task<string> GitStatusAsync(string path, CancellationToken ct);
}
public sealed class DirectoryNavigation(IManagerDbFactory factory, IPathPolicy paths, ProjectOptions options, OwnerGuard guard)
{
    public async Task InitializeRootsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        foreach (var path in options.AllowedRoots.Where(Directory.Exists))
        {
            var canonical = paths.DemandAllowed(path);
            if (!await db.Roots.AnyAsync(x => x.CanonicalPath == canonical, ct)) db.Roots.Add(new RegisteredRoot { CanonicalPath = canonical, Name = Path.GetFileName(canonical) });
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task<List<RegisteredRoot>> RootsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct); return await db.Roots.OrderBy(x => x.Name).ToListAsync(ct);
    }
    public async Task VisitAsync(string path, long owner, CancellationToken ct)
    {
        guard.Demand(owner); var canonical = paths.DemandAllowed(path);
        await using var db = await factory.CreateAsync(ct);
        var item = await db.Bookmarks.SingleOrDefaultAsync(x => x.OwnerUserId == owner && x.CanonicalPath == canonical, ct);
        if (item is null) { item = new() { OwnerUserId = owner, CanonicalPath = canonical }; db.Bookmarks.Add(item); }
        item.LastOpenedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        var old = await db.Bookmarks.Where(x => x.OwnerUserId == owner && !x.IsFavorite).OrderByDescending(x => x.LastOpenedAtUtc).Skip(50).Select(x => x.Id).ToListAsync(ct);
        if (old.Count > 0) await db.Bookmarks.Where(x => old.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }
    public async Task ToggleFavoriteAsync(string path, long owner, CancellationToken ct)
    {
        await VisitAsync(path, owner, ct); var canonical = paths.DemandAllowed(path);
        await using var db = await factory.CreateAsync(ct); var item = await db.Bookmarks.SingleAsync(x => x.OwnerUserId == owner && x.CanonicalPath == canonical, ct); item.IsFavorite = !item.IsFavorite; await db.SaveChangesAsync(ct);
    }
    public async Task<List<DirectoryBookmark>> BookmarksAsync(bool favorites, long owner, CancellationToken ct)
    {
        guard.Demand(owner); await using var db = await factory.CreateAsync(ct);
        return await db.Bookmarks.Where(x => x.OwnerUserId == owner && (!favorites || x.IsFavorite)).OrderByDescending(x => x.LastOpenedAtUtc).Take(50).ToListAsync(ct);
    }
}
