using OpenCodeManager.Application;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.IntegrationTests;
public sealed class TestFactory(string path) : IManagerDbFactory
{
    public Task<IManagerDb> CreateAsync(CancellationToken ct = default) => Task.FromResult<IManagerDb>(new ManagerDbContext(ManagerDbContext.Options(path)));
}
