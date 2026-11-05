using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class PermissionManager(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, OwnerGuard guard, ILogger<PermissionManager> logger)
{
    private readonly SemaphoreSlim gate = new(1);
    public bool CanAutoApprove(string action, IEnumerable<string> resources, string workingDirectory)
    {
        if (action is not ("read" or "edit"))
            return false;
        var root = paths.DemandAllowed(workingDirectory);
        var items = resources.ToArray();
        if (items.Length == 0)
            return false;
        foreach (var resource in items)
        {
            if (resource.IndexOfAny(['*', '?', '[', '\0', '\n']) >= 0)
                return false;
            string canonical;
            try
            {
                canonical = paths.DemandAllowed(Path.IsPathRooted(resource) ? resource : Path.Combine(root, resource));
            }
            catch (Exception e)when (e is UnauthorizedAccessException or IOException or ArgumentException)
            {
                return false;
            }

            if (!canonical.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return false;
            var name = Path.GetFileName(canonical);
            if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || canonical.Split('/').Any(p => p.Equals("secrets", StringComparison.OrdinalIgnoreCase)) || name.Contains("credential", StringComparison.OrdinalIgnoreCase) || name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || name is "auth.json" or "service.json" or "id_rsa" or "id_ed25519")
                return false;
        }

        return true;
    }

    public async Task SyncAsync(AgentTask task, AgentSession session, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var pending = await api.GetPermissionsAsync(session.OpenCodeSessionId, ct);
            await using var db = await factory.CreateAsync(ct);
            foreach (var remote in pending)
            {
                if (remote.SessionId != session.OpenCodeSessionId)
                    continue;
                var request = await db.Permissions.SingleOrDefaultAsync(x => x.OpenCodeSessionId == remote.SessionId && x.OpenCodeRequestId == remote.Id, ct);
                if (request is null)
                {
                    request = new PermissionRequest
                    {
                        TaskId = task.Id,
                        OpenCodeRequestId = remote.Id,
                        OpenCodeSessionId = remote.SessionId,
                        Action = remote.Action,
                        Resource = string.Join("\n", remote.Resources),
                        Status = ApprovalStatus.Pending
                    };
                    db.Permissions.Add(request);
                    await db.SaveChangesAsync(ct);
                }

                if (request.Status != ApprovalStatus.Pending)
                    continue;
                if (CanAutoApprove(remote.Action, remote.Resources, session.WorkingDirectory))
                {
                    request.Status = ApprovalStatus.Resolving;
                    await db.SaveChangesAsync(ct);
                    try
                    {
                        await api.ReplyPermissionAsync(remote.SessionId, remote.Id, "once", ct);
                        request.Status = ApprovalStatus.Approved;
                        request.ResolvedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                    catch
                    {
                        request.Status = ApprovalStatus.Unknown;
                        await db.SaveChangesAsync(CancellationToken.None);
                        throw;
                    }
                }
            }

            var pendingIds = pending.Select(x => x.Id).ToHashSet();
            foreach (var stale in await db.Permissions.Where(x => x.TaskId == task.Id && (x.Status == ApprovalStatus.Pending || x.Status == ApprovalStatus.Resolving)).ToListAsync(ct))
                if (!pendingIds.Contains(stale.OpenCodeRequestId))
                {
                    stale.Status = ApprovalStatus.Unknown;
                    stale.ResolvedAtUtc = DateTime.UtcNow;
                }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReplyAsync(Guid requestId, bool approve, bool limitedRule, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateAsync(ct);
            var r = await db.Permissions.SingleAsync(x => x.Id == requestId, ct);
            if (r.Status != ApprovalStatus.Pending)
                throw new InvalidOperationException("Permission already processed or requires reconciliation");
            var t = await db.Tasks.SingleAsync(x => x.Id == r.TaskId, ct);
            if (!TaskStates.IsActive(t.Status))
                throw new InvalidOperationException("Task is no longer active");
            var s = await db.Sessions.SingleAsync(x => x.Id == t.SessionId, ct);
            paths.DemandAllowed(s.WorkingDirectory);
            if (limitedRule && !CanAutoApprove(r.Action, r.Resource.Split('\n'), s.WorkingDirectory))
                throw new InvalidOperationException("Persistent permission is restricted to safe project files");
            var remote = (await api.GetPermissionsAsync(r.OpenCodeSessionId, ct)).SingleOrDefault(x => x.Id == r.OpenCodeRequestId);
            if (remote is null || remote.Action != r.Action || string.Join("\n", remote.Resources) != r.Resource)
                throw new InvalidOperationException("Permission changed; refresh first");
            r.Status = ApprovalStatus.Resolving;
            await db.SaveChangesAsync(ct);
            try
            {
                // Never use native 'always': OpenCode may expand its save patterns.
                if (limitedRule)
                {
                    var current = await api.GetSessionAsync(s.OpenCodeSessionId, ct);
                    var rules = current.Permissions ?? SessionPermissionPolicy.CreateRules();
                    foreach (var resource in remote.Resources)
                        rules.Add(new(r.Action, resource, "allow"));
                    await api.UpdateSessionAsync(s.OpenCodeSessionId, null, null, rules, ct);
                }

                await api.ReplyPermissionAsync(r.OpenCodeSessionId, r.OpenCodeRequestId, approve ? "once" : "reject", ct);
                r.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
                r.ApprovedByTelegramUserId = owner;
                r.ResolvedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Permission {PermissionId} resolved", r.Id);
            }
            catch
            {
                r.Status = ApprovalStatus.Unknown;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
