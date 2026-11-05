using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public interface IManagerDb : IAsyncDisposable
{
    DbSet<Project> Projects { get; }

    DbSet<AgentSession> Sessions { get; }

    DbSet<AgentTask> Tasks { get; }

    DbSet<TaskAttempt> Attempts { get; }

    DbSet<TaskEvent> Events { get; }

    DbSet<TelegramMessage> TelegramMessages { get; }

    DbSet<PermissionRequest> Permissions { get; }

    DbSet<NavigationState> Navigation { get; }

    DbSet<FileAttachment> Attachments { get; }

    DbSet<FileTransfer> Transfers { get; }

    DbSet<TaskFile> TaskFiles { get; }

    DbSet<RegisteredRoot> Roots { get; }

    DbSet<DirectoryBookmark> Bookmarks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
