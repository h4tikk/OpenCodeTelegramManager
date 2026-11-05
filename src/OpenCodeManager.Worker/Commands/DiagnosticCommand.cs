using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.Worker;
public sealed class DiagnosticCommand(IOpenCodeClient api, IDbContextFactory<ManagerDbContext> factory, SessionManager sessions, WorkerConfiguration configuration)
{
    public async Task ExecuteAsync(bool synchronize, CancellationToken ct)
    {
        var info = await api.GetInfoAsync(ct);
        if (!info.Version.StartsWith("2."))
            throw new InvalidOperationException("Unsupported OpenCode major version");
        var models = await api.GetModelsAsync(null, ct);
        var agents = await api.GetAgentsAsync(null, ct);
        var remoteProjects = await api.GetProjectsAsync(ct);
        int count = 0;
        string? cursor = null;
        do
        {
            var page = await api.GetSessionsAsync(cursor, ct);
            count += page.Data.Count;
            cursor = page.Cursor?.Next;
        }
        while (cursor is not null);
        if (synchronize)
            await sessions.SyncAsync(true, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var report = new
        {
            openCode = info.Version,
            models = models.Count,
            agents = agents.Count,
            projects = remoteProjects.Count,
            sessions = count,
            sqlite = "OK",
            database = configuration.Storage.DatabasePath,
            migrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray(),
            telegramConfigured = configuration.TelegramConfigured,
            localProjects = await db.Projects.CountAsync(ct),
            mappedSessions = await db.Sessions.CountAsync(ct)
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
