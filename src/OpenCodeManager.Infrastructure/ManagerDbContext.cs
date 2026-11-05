using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;

public sealed class ManagerDbContext(DbContextOptions<ManagerDbContext> options) : DbContext(options), IManagerDb
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AgentSession> Sessions => Set<AgentSession>();
    public DbSet<AgentTask> Tasks => Set<AgentTask>();
    public DbSet<TaskAttempt> Attempts => Set<TaskAttempt>();
    public DbSet<TaskEvent> Events => Set<TaskEvent>();
    public DbSet<TelegramMessage> TelegramMessages => Set<TelegramMessage>();
    public DbSet<PermissionRequest> Permissions => Set<PermissionRequest>();
    public DbSet<NavigationState> Navigation => Set<NavigationState>();
    public DbSet<FileAttachment> Attachments => Set<FileAttachment>();
    public DbSet<FileTransfer> Transfers => Set<FileTransfer>();
    public DbSet<TaskFile> TaskFiles => Set<TaskFile>();
    public DbSet<RegisteredRoot> Roots => Set<RegisteredRoot>();
    public DbSet<DirectoryBookmark> Bookmarks => Set<DirectoryBookmark>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Project>().HasIndex(x => x.AbsolutePath).IsUnique();
        m.Entity<Project>().HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentProjectId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AgentSession>().HasIndex(x => x.OpenCodeSessionId).IsUnique();
        m.Entity<AgentSession>().HasIndex(x => x.ProjectId).IsUnique().HasFilter("IsMain = 1");
        m.Entity<AgentSession>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AgentSession>().HasOne<AgentSession>().WithMany().HasForeignKey(x => x.ParentSessionId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AgentTask>().HasOne<AgentSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AgentTask>().HasIndex(x => x.DeduplicationKey).IsUnique();
        m.Entity<AgentTask>().HasIndex(x => new { x.Status, x.CreatedAtUtc });
        m.Entity<AgentTask>().HasIndex(x => x.LockKey).IsUnique().HasFilter("Status IN (1, 2, 3, 7)");
        m.Entity<TaskAttempt>().HasOne<AgentTask>().WithMany(x => x.Attempts).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<TaskAttempt>().HasIndex(x => new { x.TaskId, x.AttemptNumber }).IsUnique();
        m.Entity<TaskAttempt>().HasIndex(x => x.OpenCodeMessageId).IsUnique();
        m.Entity<TaskEvent>().HasOne<TaskAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<TaskEvent>().HasIndex(x => new { x.AttemptId, x.OpenCodeEventId }).IsUnique();
        m.Entity<TelegramMessage>().HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<TelegramMessage>().HasIndex(x => new { x.TaskId, x.ChatId, x.Purpose }).IsUnique();
        m.Entity<PermissionRequest>().HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<PermissionRequest>().HasIndex(x => new { x.OpenCodeSessionId, x.OpenCodeRequestId }).IsUnique();
        m.Entity<NavigationState>().HasIndex(x => x.UserId).IsUnique();
        m.Entity<FileAttachment>().HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<FileTransfer>().HasOne<FileAttachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<TaskFile>().HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<TaskFile>().HasOne<FileAttachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<TaskFile>().HasIndex(x => new { x.TaskId, x.AttachmentId }).IsUnique();
        m.Entity<RegisteredRoot>().HasIndex(x => x.CanonicalPath).IsUnique();
        m.Entity<DirectoryBookmark>().HasIndex(x => new { x.OwnerUserId, x.CanonicalPath }).IsUnique();
        foreach (var entity in m.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    }
    public static DbContextOptions<ManagerDbContext> Options(string path) => new DbContextOptionsBuilder<ManagerDbContext>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 15, ForeignKeys = true }.ToString()).Options;
}
public sealed class ManagerDbFactory(IDbContextFactory<ManagerDbContext> factory) : IManagerDbFactory
{
    public async Task<IManagerDb> CreateAsync(CancellationToken ct = default) => await factory.CreateDbContextAsync(ct);
}
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ManagerDbContext>
{
    public ManagerDbContext CreateDbContext(string[] args) => new(ManagerDbContext.Options(Environment.GetEnvironmentVariable("Storage__DatabasePath") ?? new StorageOptions().DatabasePath));
}
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(ManagerDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=15000;", ct);
    }
}
