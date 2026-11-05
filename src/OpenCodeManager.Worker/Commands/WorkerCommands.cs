using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Application;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.Worker;
public sealed class WorkerCommands(IDbContextFactory<ManagerDbContext> factory, DirectoryNavigation directories, SessionManager sessions, IFileManager files, DiagnosticCommand diagnostics, WorkerConfiguration configuration, ILogger<WorkerCommands> logger)
{
    public async Task<int> ExecuteAsync(IHost host, string command, CancellationToken ct)
    {
        if (command != "--doctor")
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await DatabaseInitializer.InitializeAsync(db, ct);
            await directories.InitializeRootsAsync(ct);
        }

        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(configuration.Storage.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        if (command == "--migrate")
        {
            Console.WriteLine("SQLite migrations applied; database=" + configuration.Storage.DatabasePath);
            return 0;
        }

        if (command is "--doctor" or "--sync")
        {
            await diagnostics.ExecuteAsync(command == "--sync", ct);
            return 0;
        }

        if (command != "run")
        {
            Console.Error.WriteLine("Commands: run, --doctor, --sync, --migrate, --check-config, --health");
            return 64;
        }

        if (!configuration.TelegramConfigured)
            logger.LogWarning("Waiting for Telegram__Token and BotSecurity__OwnerUserId; Telegram and dispatcher are disabled");
        try
        {
            await sessions.SyncAsync(true, ct);
        }
        catch (Exception error)
        {
            logger.LogWarning("Initial OpenCode synchronization unavailable: {ErrorType}", error.GetType().Name);
        }

        await files.CleanupAsync(ct);
        await host.RunAsync(ct);
        return 0;
    }
}
