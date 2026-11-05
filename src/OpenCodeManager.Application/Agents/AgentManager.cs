using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class AgentManager(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, OwnerGuard guard)
{
    public async Task SelectAgentAsync(Guid? sessionId, string agent, long owner, CancellationToken ct, bool taskOnly = false)
    {
        guard.Demand(owner);

        await using var db = await factory.CreateAsync(ct);

        AgentSession? s = sessionId is null ? null : await db.Sessions.SingleAsync(x => x.Id == sessionId, ct);

        if (s is not null)
            paths.DemandAllowed(s.WorkingDirectory);
        if (!(await api.GetAgentsAsync(s?.WorkingDirectory, ct)).Any(x => x.Id == agent && !x.Hidden && x.Mode != "subagent"))
            throw new ArgumentException("Agent unavailable");
        if (taskOnly)
            (await db.Navigation.SingleAsync(x => x.UserId == owner, ct)).DraftAgent = agent;
        else if (s is not null)
            s.AgentName = agent;
        else
            (await db.Navigation.SingleAsync(x => x.UserId == owner, ct)).DefaultAgent = agent;

        await db.SaveChangesAsync(ct);
    }
}
