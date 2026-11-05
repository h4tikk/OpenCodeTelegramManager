using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class ModelManager(IManagerDbFactory factory, IOpenCodeClient api, IPathPolicy paths, OwnerGuard guard)
{
    public async Task SelectModelAsync(Guid? sessionId, ModelRef model, long owner, CancellationToken ct, bool taskOnly = false)
    {
        guard.Demand(owner);

        await using var db = await factory.CreateAsync(ct);

        AgentSession? s = sessionId is null ? null : await db.Sessions.SingleAsync(x => x.Id == sessionId, ct);

        if (s is not null)
            paths.DemandAllowed(s.WorkingDirectory);
        if (!(await api.GetModelsAsync(s?.WorkingDirectory, ct)).Any(x => x.Id == model.Id && x.ProviderId == model.ProviderId && x.Enabled))
            throw new ArgumentException("Model unavailable in catalog");
        if (taskOnly)
        {
            var nav = await db.Navigation.SingleAsync(x => x.UserId == owner, ct);
            nav.DraftProviderId = model.ProviderId;
            nav.DraftModelId = model.Id;
        }
        else if (s is not null)
        {
            s.ProviderId = model.ProviderId;
            s.ModelId = model.Id;
        }
        else
        {
            var nav = await db.Navigation.SingleAsync(x => x.UserId == owner, ct);
            nav.DefaultProviderId = model.ProviderId;
            nav.DefaultModelId = model.Id;
        }

        await db.SaveChangesAsync(ct);
    }
}
