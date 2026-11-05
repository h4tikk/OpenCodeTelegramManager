using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class ProjectManager(IManagerDbFactory factory, IPathPolicy paths, OwnerGuard guard, ILogger<ProjectManager> logger, IProjectTechnologyDetector technology)
{
    private readonly SemaphoreSlim syncLock = new(1);
    public async Task<int> DiscoverAsync(CancellationToken ct)
    {
        await syncLock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var known = (await db.Projects.ToListAsync(ct)).ToDictionary(x => x.AbsolutePath);
            var count = 0;
            foreach (var item in paths.Discover())
            {
                if (known.ContainsKey(item.Path))
                    continue;
                var p = new Project
                {
                    Name = Path.GetFileName(item.Path),
                    AbsolutePath = item.Path,
                    Kind = item.Kind,
                    ParentProjectId = item.Parent is not null && known.TryGetValue(item.Parent, out var parent) ? parent.Id : null,
                    Technology = technology.Detect(item.Path)
                };
                db.Projects.Add(p);
                known.Add(item.Path, p);
                count++;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Project discovery added {Count}", count);
            return count;
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task<Project> AddProjectAsync(string path, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        var canonical = paths.DemandAllowed(path);
        if (!Directory.Exists(canonical))
            throw new DirectoryNotFoundException();
        await using var db = await factory.CreateAsync(ct);
        var existing = await db.Projects.SingleOrDefaultAsync(x => x.AbsolutePath == canonical, ct);
        if (existing is not null)
            return existing;
        var p = new Project
        {
            Name = Path.GetFileName(canonical),
            AbsolutePath = canonical,
            Kind = ProjectKind.Directory,
            Technology = technology.Detect(canonical)
        };
        db.Projects.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }
}
