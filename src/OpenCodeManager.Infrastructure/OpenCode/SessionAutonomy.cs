using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class SessionAutonomy(IOpenCodeClient api, IManagerDbFactory factory, IPathPolicy paths, AutonomyOptions options) : ISessionAutonomy
{
    public bool Enabled => options.Enabled;

    public const string PluginId = "opencode.manager.autonomy.v1";
    public async Task ConfigureAsync(OpenCodeSession session, string directory, CancellationToken ct)
    {
        var canonical = paths.DemandAllowed(directory);
        if (session.Location.Directory is null || paths.Canonicalize(session.Location.Directory) != canonical)
            throw new InvalidOperationException("Autonomy session directory mismatch");
        if (options.Enabled)
        {
            bool active = false;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                active = (await api.GetPluginsAsync(canonical, ct)).Any(x => x.Id == PluginId && x.State.Status == "active");
                if (active)
                    break;
                if (attempt < 5)
                    await Task.Delay(250, ct);
            }

            if (!active)
                throw new AutonomyUnavailableException("Изолирующий плагин OpenCode не активен. Задача не отправлена; восстановите автономный профиль.");
        }

        var metadata = new Dictionary<string, JsonElement>(session.Metadata ?? []);
        metadata["opencodeManagerAutonomy"] = JsonSerializer.SerializeToElement(new { version = 1, enabled = options.Enabled, directory = canonical });
        await api.SetSessionMetadataAsync(session.Id, metadata, ct);
    }

    public async Task ConfigureActiveTasksAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var ids = await db.Tasks.Where(x => x.Status == OpenCodeManager.Domain.AgentTaskStatus.Running || x.Status == OpenCodeManager.Domain.AgentTaskStatus.AwaitingApproval || x.Status == OpenCodeManager.Domain.AgentTaskStatus.NeedsReconciliation).Select(x => x.SessionId).Distinct().ToListAsync(ct);
        foreach (var session in await db.Sessions.Where(x => ids.Contains(x.Id)).ToListAsync(ct))
            await ConfigureAsync(await api.GetSessionAsync(session.OpenCodeSessionId, ct), session.WorkingDirectory, ct);
    }
}
