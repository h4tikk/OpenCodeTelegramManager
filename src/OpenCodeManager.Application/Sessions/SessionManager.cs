using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class SessionManager(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, OwnerGuard guard, ProjectManager projects, ILogger<SessionManager> logger)
{
    private readonly SemaphoreSlim syncLock = new(1);
    public async Task SyncAsync(bool createMain, CancellationToken ct)
    {
        await projects.DiscoverAsync(ct);
        await syncLock.WaitAsync(ct);
        try
        {
            var info = await api.GetInfoAsync(ct);
            if (!info.Version.StartsWith("2."))
                throw new InvalidOperationException("Only verified OpenCode v2 API is supported");
            await using var db = await factory.CreateAsync(ct);
            var projects = await db.Projects.ToListAsync(ct);
            var remoteProjects = await api.GetProjectsAsync(ct);
            foreach (var p in projects)
                p.OpenCodeProjectId = remoteProjects.FirstOrDefault(x => SafeSamePath(x.Canonical, p.AbsolutePath))?.Id ?? p.OpenCodeProjectId;
            var known = (await db.Sessions.ToListAsync(ct)).ToDictionary(x => x.OpenCodeSessionId);
            string? cursor = null;
            var cursors = new HashSet<string>();
            do
            {
                var page = await api.GetSessionsAsync(cursor, ct);
                foreach (var remote in page.Data)
                {
                    if (remote.Location.Directory is null)
                        continue;
                    string directory;
                    try
                    {
                        directory = paths.DemandAllowed(remote.Location.Directory);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    var project = projects.SingleOrDefault(x => x.AbsolutePath == directory);
                    if (project is null)
                    {
                        project = new Project
                        {
                            AbsolutePath = directory,
                            Name = Path.GetFileName(directory),
                            Kind = ProjectKind.Directory,
                            OpenCodeProjectId = remote.ProjectId
                        };
                        db.Projects.Add(project);
                        projects.Add(project);
                    }

                    if (!known.TryGetValue(remote.Id, out var session))
                    {
                        session = new AgentSession
                        {
                            OpenCodeSessionId = remote.Id,
                            ProjectId = project.Id,
                            CreatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds((long)remote.Time.Created).UtcDateTime
                        };
                        db.Sessions.Add(session);
                        known.Add(remote.Id, session);
                    }

                    session.Title = remote.Title ?? remote.Id;
                    session.WorkingDirectory = directory;
                    session.ProviderId = remote.Model?.ProviderId;
                    session.ModelId = remote.Model?.Id;
                    session.AgentName = remote.Agent;
                    session.LastSyncedAtUtc = DateTime.UtcNow;
                    if (project.Kind == ProjectKind.Category && session.Title == project.Name + " — Main" && !known.Values.Any(x => x.ProjectId == project.Id && x.IsMain && x.Id != session.Id))
                        session.IsMain = true;
                }

                cursor = page.Cursor?.Next;
            }
            while (cursor is not null && cursors.Add(cursor));
            await db.SaveChangesAsync(ct);
            if (createMain)
                foreach (var project in projects.Where(x => x.Kind == ProjectKind.Category))
                    if (!known.Values.Any(x => x.ProjectId == project.Id && x.IsMain))
                    {
                        var main = await CreateRemoteSession(project, project.Name + " — Main", true, null, ct);
                        db.Sessions.Add(main);
                        known.Add(main.OpenCodeSessionId, main);
                        await db.SaveChangesAsync(ct);
                    }

            logger.LogInformation("Session synchronization complete: {Count} mapped", known.Count);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task SyncDirectoryAsync(string directory, CancellationToken ct)
    {
        var canonical = paths.DemandAllowed(directory);
        await syncLock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var project = await db.Projects.SingleAsync(x => x.AbsolutePath == canonical, ct);
            string? cursor = null;
            var seen = new HashSet<string>();
            do
            {
                var page = await api.GetDirectorySessionsAsync(canonical, cursor, ct);
                foreach (var remote in page.Data)
                {
                    if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != canonical)
                        continue;
                    var s = await db.Sessions.SingleOrDefaultAsync(x => x.OpenCodeSessionId == remote.Id, ct);
                    if (s is null)
                    {
                        s = new()
                        {
                            OpenCodeSessionId = remote.Id,
                            ProjectId = project.Id,
                            WorkingDirectory = canonical
                        };
                        db.Sessions.Add(s);
                    }

                    s.Title = remote.Title ?? remote.Id;
                    s.AgentName = remote.Agent;
                    s.ModelId = remote.Model?.Id;
                    s.ProviderId = remote.Model?.ProviderId;
                    s.LastSyncedAtUtc = DateTime.UtcNow;
                }

                cursor = page.Cursor?.Next;
            }
            while (cursor is not null && seen.Add(cursor));
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            syncLock.Release();
        }
    }

    private bool SafeSamePath(string a, string b)
    {
        try
        {
            return paths.Canonicalize(a) == b;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task<AgentSession> CreateRemoteSession(Project project, string title, bool main, Guid? parent, CancellationToken ct)
    {
        var dir = paths.DemandAllowed(project.AbsolutePath);
        // Stable main-session ID makes an ambiguous creation recoverable without a duplicate POST.
        var remoteId = "ses_manager_" + (main ? project.Id.ToString("N") : Guid.NewGuid().ToString("N"));
        OpenCodeSession remote;
        try
        {
            remote = await api.GetSessionAsync(remoteId, ct);
        }
        catch (OpenCodeApiException e)when (e.StatusCode == 404)
        {
            remote = await api.CreateSessionAsync(new(remoteId, title, new(dir), SessionPermissionPolicy.CreateRules()), ct);
        }

        if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != dir)
            throw new InvalidOperationException("OpenCode returned a different working directory");
        return new AgentSession
        {
            OpenCodeSessionId = remote.Id,
            ProjectId = project.Id,
            ParentSessionId = parent,
            Title = title,
            WorkingDirectory = dir,
            IsMain = main,
            Managed = true,
            AgentName = remote.Agent,
            ModelId = remote.Model?.Id,
            ProviderId = remote.Model?.ProviderId,
            LastSyncedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<AgentSession> CreateSessionAsync(Guid projectId, string title, Guid? parent, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 150)
            throw new ArgumentException("Session title must be 1–150 characters");
        await syncLock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var project = await db.Projects.SingleAsync(x => x.Id == projectId, ct);
            if (parent.HasValue && !await db.Sessions.AnyAsync(x => x.Id == parent, ct))
                throw new ArgumentException("Unknown parent session");
            var session = await CreateRemoteSession(project, title.Trim(), false, parent, ct);
            db.Sessions.Add(session);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Session {SessionId} created for {ProjectId}", session.Id, projectId);
            return session;
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task RenameAsync(Guid id, string title, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 150)
            throw new ArgumentException("Invalid title");
        await using var db = await factory.CreateAsync(ct);
        var s = await db.Sessions.SingleAsync(x => x.Id == id, ct);
        paths.DemandAllowed(s.WorkingDirectory);
        await api.UpdateSessionAsync(s.OpenCodeSessionId, title.Trim(), null, null, ct);
        s.Title = title.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task HideAsync(Guid id, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await using var db = await factory.CreateAsync(ct);
        var s = await db.Sessions.SingleAsync(x => x.Id == id, ct);
        s.Hidden = !s.Hidden;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> HistoryAsync(Guid sessionId, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var s = await db.Sessions.SingleAsync(x => x.Id == sessionId, ct);
        paths.DemandAllowed(s.WorkingDirectory);
        var page = await api.GetMessagesAsync(s.OpenCodeSessionId, null, ct);
        return string.Join("\n\n", page.Data.Take(30).Reverse().Select(x => x.Type + ": " + (x.Text ?? string.Join("\n", x.Content?.Where(p => p.Type == "text").Select(p => p.Text) ?? []))));
    }
}
