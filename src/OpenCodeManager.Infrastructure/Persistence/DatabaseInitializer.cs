using Microsoft.EntityFrameworkCore;

namespace OpenCodeManager.Infrastructure;
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(ManagerDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=15000;", ct);
    }
}
