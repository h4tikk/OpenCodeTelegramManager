using Microsoft.EntityFrameworkCore.Design;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ManagerDbContext>
{
    public ManagerDbContext CreateDbContext(string[] args) => new(ManagerDbContext.Options(Environment.GetEnvironmentVariable("Storage__DatabasePath") ?? new StorageOptions().DatabasePath));
}
