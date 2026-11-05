using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class ManagerDbFactory(IDbContextFactory<ManagerDbContext> factory) : IManagerDbFactory
{
    public async Task<IManagerDb> CreateAsync(CancellationToken ct = default) => await factory.CreateDbContextAsync(ct);
}
