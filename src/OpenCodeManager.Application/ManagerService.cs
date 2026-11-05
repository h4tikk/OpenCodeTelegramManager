using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;
namespace OpenCodeManager.Application;

public sealed class ManagerService(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, OwnerGuard guard, ILogger<ManagerService> logger)
{
    private readonly SemaphoreSlim syncLock = new(1);
    public static List<PermissionRule> SessionRules() => [new("*", "*", "ask"), new("external_directory", "*", "deny"), new("read", "*.env", "deny"), new("read", "*.env.*", "deny"), new("read", "*/.ssh/*", "deny"), new("read", "*/.gnupg/*", "deny"), new("edit", "*.env*", "deny")];
    public async Task<NavigationState> NavigationAsync(long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await using var db = await factory.CreateAsync(ct);
        var state = await db.Navigation.SingleOrDefaultAsync(x => x.UserId == owner, ct);
        if (state is not null) return state;
        state = new NavigationState { UserId = owner }; db.Navigation.Add(state); await db.SaveChangesAsync(ct); return state;
    }
    public async Task SaveNavigationAsync(NavigationState state, CancellationToken ct)
    {
        guard.Demand(state.UserId); await using var db = await factory.CreateAsync(ct);
        db.Navigation.Update(state); await db.SaveChangesAsync(ct);
    }
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
                if (known.ContainsKey(item.Path)) continue;
                var p = new Project { Name = Path.GetFileName(item.Path), AbsolutePath = item.Path, Kind = item.Kind, ParentProjectId = item.Parent is not null && known.TryGetValue(item.Parent, out var parent) ? parent.Id : null, Technology = DetectTechnology(item.Path) };
                db.Projects.Add(p); known.Add(item.Path, p); count++;
            }
            await db.SaveChangesAsync(ct); logger.LogInformation("Project discovery added {Count}", count); return count;
        }
        finally { syncLock.Release(); }
    }
    private static string? DetectTechnology(string path)
    {
        var files = Directory.EnumerateFiles(path).Select(Path.GetFileName).ToArray();
        var tags = new List<string>();
        if (files.Any(x => x!.EndsWith(".csproj") || x.EndsWith(".slnx") || x.EndsWith(".sln"))) tags.Add("C#");
        if (files.Contains("Gemfile") || files.Any(x => x!.EndsWith(".rb"))) tags.Add("Ruby");
        if (files.Contains("Cargo.toml")) tags.Add("Rust");
        if (files.Contains("pyproject.toml") || files.Any(x => x!.EndsWith(".py"))) tags.Add("Python");
        if (files.Contains("CMakeLists.txt") || files.Any(x => x!.EndsWith(".cpp"))) tags.Add("C++");
        return tags.Count > 0 ? string.Join(", ", tags) : null;
    }
    public async Task<Project> AddProjectAsync(string path, long owner, CancellationToken ct)
    {
        guard.Demand(owner); var canonical = paths.DemandAllowed(path);
        if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException();
        await using var db = await factory.CreateAsync(ct);
        var existing = await db.Projects.SingleOrDefaultAsync(x => x.AbsolutePath == canonical, ct); if (existing is not null) return existing;
        var p = new Project { Name = Path.GetFileName(canonical), AbsolutePath = canonical, Kind = ProjectKind.Directory, Technology = DetectTechnology(canonical) };
        db.Projects.Add(p); await db.SaveChangesAsync(ct); return p;
    }
    public async Task SyncAsync(bool createMain, CancellationToken ct)
    {
        await DiscoverAsync(ct);
        await syncLock.WaitAsync(ct);
        try
        {
            var info = await api.GetInfoAsync(ct); if (!info.Version.StartsWith("2.")) throw new InvalidOperationException("Only verified OpenCode v2 API is supported");
            await using var db = await factory.CreateAsync(ct);
            var projects = await db.Projects.ToListAsync(ct);
            var remoteProjects = await api.GetProjectsAsync(ct);
            foreach (var p in projects) p.OpenCodeProjectId = remoteProjects.FirstOrDefault(x => SafeSamePath(x.Canonical, p.AbsolutePath))?.Id ?? p.OpenCodeProjectId;
            var known = (await db.Sessions.ToListAsync(ct)).ToDictionary(x => x.OpenCodeSessionId);
            string? cursor = null;
            var cursors = new HashSet<string>();
            do
            {
                var page = await api.GetSessionsAsync(cursor, ct);
                foreach (var remote in page.Data)
                {
                    if (remote.Location.Directory is null) continue;
                    string directory;
                    try { directory = paths.DemandAllowed(remote.Location.Directory); } catch (UnauthorizedAccessException) { continue; }
                    var project = projects.SingleOrDefault(x => x.AbsolutePath == directory);
                    if (project is null)
                    {
                        project = new Project { AbsolutePath = directory, Name = Path.GetFileName(directory), Kind = ProjectKind.Directory, OpenCodeProjectId = remote.ProjectId };
                        db.Projects.Add(project); projects.Add(project);
                    }
                    if (!known.TryGetValue(remote.Id, out var session))
                    {
                        session = new AgentSession { OpenCodeSessionId = remote.Id, ProjectId = project.Id, CreatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds((long)remote.Time.Created).UtcDateTime };
                        db.Sessions.Add(session); known.Add(remote.Id, session);
                    }
                    session.Title = remote.Title ?? remote.Id; session.WorkingDirectory = directory;
                    session.ProviderId = remote.Model?.ProviderId; session.ModelId = remote.Model?.Id; session.AgentName = remote.Agent;
                    session.LastSyncedAtUtc = DateTime.UtcNow;
                    if (project.Kind == ProjectKind.Category && session.Title == project.Name + " — Main" && !known.Values.Any(x => x.ProjectId == project.Id && x.IsMain && x.Id != session.Id)) session.IsMain = true;
                }
                cursor = page.Cursor?.Next;
            } while (cursor is not null && cursors.Add(cursor));
            await db.SaveChangesAsync(ct);
            if (createMain)
                foreach (var project in projects.Where(x => x.Kind == ProjectKind.Category))
                    if (!known.Values.Any(x => x.ProjectId == project.Id && x.IsMain))
                    {
                        var main = await CreateRemoteSession(project, project.Name + " — Main", true, null, ct);
                        db.Sessions.Add(main); known.Add(main.OpenCodeSessionId, main); await db.SaveChangesAsync(ct);
                    }
            logger.LogInformation("Session synchronization complete: {Count} mapped", known.Count);
        }
        finally { syncLock.Release(); }
    }
    public async Task SyncDirectoryAsync(string directory, CancellationToken ct)
    {
        var canonical = paths.DemandAllowed(directory);
        await syncLock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var project = await db.Projects.SingleAsync(x => x.AbsolutePath == canonical, ct);
            string? cursor = null; var seen = new HashSet<string>();
            do
            {
                var page = await api.GetDirectorySessionsAsync(canonical, cursor, ct);
                foreach (var remote in page.Data)
                {
                    if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != canonical) continue;
                    var s = await db.Sessions.SingleOrDefaultAsync(x => x.OpenCodeSessionId == remote.Id, ct);
                    if (s is null) { s = new() { OpenCodeSessionId = remote.Id, ProjectId = project.Id, WorkingDirectory = canonical }; db.Sessions.Add(s); }
                    s.Title = remote.Title ?? remote.Id; s.AgentName = remote.Agent; s.ModelId = remote.Model?.Id; s.ProviderId = remote.Model?.ProviderId; s.LastSyncedAtUtc = DateTime.UtcNow;
                }
                cursor = page.Cursor?.Next;
            } while (cursor is not null && seen.Add(cursor));
            await db.SaveChangesAsync(ct);
        }
        finally { syncLock.Release(); }
    }
    private bool SafeSamePath(string a, string b) { try { return paths.Canonicalize(a) == b; } catch (IOException) { return false; } }
    private async Task<AgentSession> CreateRemoteSession(Project project, string title, bool main, Guid? parent, CancellationToken ct)
    {
        var dir = paths.DemandAllowed(project.AbsolutePath);
        // Stable main-session ID makes an ambiguous creation recoverable without a duplicate POST.
        var remoteId = "ses_manager_" + (main ? project.Id.ToString("N") : Guid.NewGuid().ToString("N"));
        OpenCodeSession remote;
        try { remote = await api.GetSessionAsync(remoteId, ct); }
        catch (OpenCodeApiException e) when (e.StatusCode == 404) { remote = await api.CreateSessionAsync(new(remoteId, title, new(dir), SessionRules()), ct); }
        if (remote.Location.Directory is null || paths.Canonicalize(remote.Location.Directory) != dir) throw new InvalidOperationException("OpenCode returned a different working directory");
        return new AgentSession { OpenCodeSessionId = remote.Id, ProjectId = project.Id, ParentSessionId = parent, Title = title, WorkingDirectory = dir, IsMain = main, Managed = true, AgentName = remote.Agent, ModelId = remote.Model?.Id, ProviderId = remote.Model?.ProviderId, LastSyncedAtUtc = DateTime.UtcNow };
    }
    public async Task<AgentSession> CreateSessionAsync(Guid projectId, string title, Guid? parent, long owner, CancellationToken ct)
    {
        guard.Demand(owner); if (string.IsNullOrWhiteSpace(title) || title.Length > 150) throw new ArgumentException("Session title must be 1–150 characters");
        await syncLock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var project = await db.Projects.SingleAsync(x => x.Id == projectId, ct);
            if (parent.HasValue && !await db.Sessions.AnyAsync(x => x.Id == parent, ct)) throw new ArgumentException("Unknown parent session");
            var session = await CreateRemoteSession(project, title.Trim(), false, parent, ct);
            db.Sessions.Add(session); await db.SaveChangesAsync(ct); logger.LogInformation("Session {SessionId} created for {ProjectId}",session.Id,projectId);return session;
        }
        finally { syncLock.Release(); }
    }
    public async Task RenameAsync(Guid id, string title, long owner, CancellationToken ct)
    {
        guard.Demand(owner); if (string.IsNullOrWhiteSpace(title) || title.Length > 150) throw new ArgumentException("Invalid title");
        await using var db = await factory.CreateAsync(ct); var s = await db.Sessions.SingleAsync(x => x.Id == id, ct); paths.DemandAllowed(s.WorkingDirectory);
        await api.UpdateSessionAsync(s.OpenCodeSessionId, title.Trim(), null, null, ct); s.Title = title.Trim(); await db.SaveChangesAsync(ct);
    }
    public async Task HideAsync(Guid id, long owner, CancellationToken ct)
    {
        guard.Demand(owner); await using var db = await factory.CreateAsync(ct); var s = await db.Sessions.SingleAsync(x => x.Id == id, ct);
        s.Hidden = !s.Hidden; await db.SaveChangesAsync(ct);
    }
    public async Task<AgentTask> EnqueueAsync(Guid sessionId, string prompt, string deduplicationKey, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 100_000) throw new ArgumentException("Prompt must be 1–100000 characters");
        await using var db = await factory.CreateAsync(ct);
        var duplicate = await db.Tasks.SingleOrDefaultAsync(x => x.DeduplicationKey == deduplicationKey, ct); if (duplicate is not null) return duplicate;
        var session = await db.Sessions.SingleAsync(x => x.Id == sessionId, ct); paths.DemandAllowed(session.WorkingDirectory);
        var nav = await db.Navigation.SingleOrDefaultAsync(x => x.UserId == owner, ct);
        var t = new AgentTask { SessionId = sessionId, Title = prompt[..Math.Min(80, prompt.Length)], Prompt = prompt, DeduplicationKey = deduplicationKey, LockKey = paths.LockKey(session.WorkingDirectory), CreatedByTelegramUserId = owner, ProviderId = nav?.DraftProviderId ?? session.ProviderId ?? nav?.DefaultProviderId, ModelId = nav?.DraftModelId ?? session.ModelId ?? nav?.DefaultModelId, AgentName = nav?.DraftAgent ?? session.AgentName ?? nav?.DefaultAgent };
        db.Tasks.Add(t);
        var fileIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(nav?.PendingFileIdsJson ?? "[]") ?? [];
        foreach (var fileId in fileIds.Distinct())
        {
            var file = await db.Attachments.SingleAsync(x => x.Id == fileId && x.ExpiresAtUtc > DateTime.UtcNow, ct);
            db.TaskFiles.Add(new TaskFile { TaskId = t.Id, AttachmentId = fileId }); file.TaskId ??= t.Id;
        }
        if (nav is not null) { nav.PendingFileIdsJson = null; nav.DraftProviderId = null; nav.DraftModelId = null; nav.DraftAgent = null; }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { var replay=await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.DeduplicationKey == deduplicationKey, ct); if(replay is not null)return replay; throw; }
        logger.LogInformation("Task {TaskId} queued", t.Id); return t;
    }
    public async Task SelectModelAsync(Guid? sessionId, ModelRef model, long owner, CancellationToken ct, bool taskOnly = false)
    {
        guard.Demand(owner); await using var db = await factory.CreateAsync(ct);
        AgentSession? s = sessionId is null ? null : await db.Sessions.SingleAsync(x => x.Id == sessionId, ct);
        if (!(await api.GetModelsAsync(s?.WorkingDirectory, ct)).Any(x => x.Id == model.Id && x.ProviderId == model.ProviderId && x.Enabled)) throw new ArgumentException("Model unavailable in catalog");
        if (taskOnly) { var nav=await db.Navigation.SingleAsync(x=>x.UserId==owner,ct); nav.DraftProviderId=model.ProviderId; nav.DraftModelId=model.Id; }
        else if (s is not null) { s.ProviderId = model.ProviderId; s.ModelId = model.Id; }
        else { var nav = await db.Navigation.SingleAsync(x => x.UserId == owner, ct); nav.DefaultProviderId = model.ProviderId; nav.DefaultModelId = model.Id; }
        await db.SaveChangesAsync(ct);
    }
    public async Task SelectAgentAsync(Guid? sessionId, string agent, long owner, CancellationToken ct, bool taskOnly = false)
    {
        guard.Demand(owner); await using var db = await factory.CreateAsync(ct);
        AgentSession? s = sessionId is null ? null : await db.Sessions.SingleAsync(x => x.Id == sessionId, ct);
        if (!(await api.GetAgentsAsync(s?.WorkingDirectory, ct)).Any(x => x.Id == agent && !x.Hidden && x.Mode != "subagent")) throw new ArgumentException("Agent unavailable");
        if (taskOnly) (await db.Navigation.SingleAsync(x=>x.UserId==owner,ct)).DraftAgent=agent; else if (s is not null) s.AgentName = agent; else (await db.Navigation.SingleAsync(x => x.UserId == owner, ct)).DefaultAgent = agent;
        await db.SaveChangesAsync(ct);
    }
    public async Task CancelAsync(Guid taskId, long owner, CancellationToken ct)
    {
        guard.Demand(owner); await using var db = await factory.CreateAsync(ct); var t = await db.Tasks.SingleAsync(x => x.Id == taskId, ct);
        if (TaskStates.IsTerminal(t.Status)) return;
        if (t.Status == AgentTaskStatus.Queued)
        {
            await db.Tasks.Where(x => x.Id == taskId && x.Status == AgentTaskStatus.Queued).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, AgentTaskStatus.Cancelled).SetProperty(t => t.CompletedAtUtc, DateTime.UtcNow), ct);
        }
        await db.Tasks.Where(x => x.Id == taskId && x.Status != AgentTaskStatus.Completed && x.Status != AgentTaskStatus.Failed && x.Status != AgentTaskStatus.Cancelled).ExecuteUpdateAsync(x => x.SetProperty(t => t.CancelRequested, true), ct);
    }
    public async Task<string> HistoryAsync(Guid sessionId, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct); var s = await db.Sessions.SingleAsync(x => x.Id == sessionId, ct); paths.DemandAllowed(s.WorkingDirectory);
        var page = await api.GetMessagesAsync(s.OpenCodeSessionId, null, ct);
        return string.Join("\n\n", page.Data.Take(30).Reverse().Select(x => x.Type + ": " + (x.Text ?? string.Join("\n", x.Content?.Where(p => p.Type == "text").Select(p => p.Text) ?? []))));
    }
    public async Task<string> DiffAsync(Guid taskId, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct); var t = await db.Tasks.SingleAsync(x => x.Id == taskId, ct); var s = await db.Sessions.SingleAsync(x => x.Id == t.SessionId, ct); paths.DemandAllowed(s.WorkingDirectory);
        var attempt = await db.Attempts.Where(x => x.TaskId == taskId).OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(ct);
        return string.Join("\n", (await api.GetDiffAsync(s.OpenCodeSessionId, attempt?.OpenCodeMessageId, ct)).Select(x => x.File + "\n" + x.Patch));
    }
}
