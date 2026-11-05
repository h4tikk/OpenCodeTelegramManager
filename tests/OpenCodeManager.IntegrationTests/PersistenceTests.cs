using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.IntegrationTests;
public sealed class PersistenceTests
{
    [Fact]
    public async Task MigrationsCreateForeignKeysAndPersistQueueAcrossContexts()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            Guid taskId;
            await using (var db = new ManagerDbContext(ManagerDbContext.Options(path)))
            {
                await DatabaseInitializer.InitializeAsync(db);
                var p = new Project
                {
                    Name = "test",
                    AbsolutePath = "/tmp/test"
                };
                db.Projects.Add(p);
                var s = new AgentSession
                {
                    ProjectId = p.Id,
                    OpenCodeSessionId = "ses_test",
                    WorkingDirectory = p.AbsolutePath
                };
                db.Sessions.Add(s);
                var t = new AgentTask
                {
                    SessionId = s.Id,
                    LockKey = p.AbsolutePath,
                    Prompt = "fixture",
                    DeduplicationKey = "telegram:1"
                };
                db.Tasks.Add(t);
                taskId = t.Id;
                await db.SaveChangesAsync();
                Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
            }

            await using (var db = new ManagerDbContext(ManagerDbContext.Options(path)))
            {
                var task = await db.Tasks.SingleAsync();
                Assert.Equal(taskId, task.Id);
                Assert.Equal(AgentTaskStatus.Queued, task.Status);
                await db.Database.OpenConnectionAsync();
                using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "PRAGMA foreign_key_check";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.False(await reader.ReadAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
                File.Delete(f);
        }
    }

    [Fact]
    public async Task DatabaseRejectsDuplicatePromptKeysAndConcurrentTreeLocks()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            await using var db = new ManagerDbContext(ManagerDbContext.Options(path));
            await DatabaseInitializer.InitializeAsync(db);
            var p = new Project
            {
                AbsolutePath = "/tmp/test"
            };
            db.Projects.Add(p);
            var s = new AgentSession
            {
                ProjectId = p.Id,
                OpenCodeSessionId = "ses_1"
            };
            db.Sessions.Add(s);
            db.Tasks.Add(new() { SessionId = s.Id, LockKey = "/tree", Status = AgentTaskStatus.Running, DeduplicationKey = "same" });
            await db.SaveChangesAsync();
            db.Tasks.Add(new() { SessionId = s.Id, LockKey = "/tree", Status = AgentTaskStatus.Dispatching, DeduplicationKey = "different" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.Tasks.Add(new() { SessionId = s.Id, LockKey = "/other", DeduplicationKey = "same" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
                File.Delete(f);
        }
    }
}
