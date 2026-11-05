using Microsoft.EntityFrameworkCore;
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
        m.ApplyConfigurationsFromAssembly(typeof(ManagerDbContext).Assembly);
        foreach (var entity in m.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    }

    public static DbContextOptions<ManagerDbContext> Options(string path) => new DbContextOptionsBuilder<ManagerDbContext>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 15, ForeignKeys = true }.ToString()).Options;
}
