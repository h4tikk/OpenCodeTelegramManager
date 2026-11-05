using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public sealed class NavigationService(IManagerDbFactory factory, OwnerGuard guard)
{
    public async Task<NavigationState> NavigationAsync(long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await using var db = await factory.CreateAsync(ct);
        var state = await db.Navigation.SingleOrDefaultAsync(x => x.UserId == owner, ct);
        if (state is not null)
            return state;
        state = new NavigationState
        {
            UserId = owner
        };
        db.Navigation.Add(state);
        await db.SaveChangesAsync(ct);
        return state;
    }

    public async Task SaveNavigationAsync(NavigationState state, CancellationToken ct)
    {
        guard.Demand(state.UserId);
        await using var db = await factory.CreateAsync(ct);
        db.Navigation.Update(state);
        await db.SaveChangesAsync(ct);
    }
}
