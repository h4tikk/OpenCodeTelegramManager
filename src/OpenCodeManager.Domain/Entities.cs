namespace OpenCodeManager.Domain;

public enum ProjectKind { Category, Repository, Directory }
public enum AgentTaskStatus { Queued, Dispatching, Running, AwaitingApproval, Completed, Failed, Cancelled, NeedsReconciliation }
public enum AttemptStatus { Dispatching, Running, Completed, Failed, Cancelled, Uncertain }
public enum ApprovalStatus { Pending, Resolving, Approved, Rejected, Unknown }
public abstract class Entity { public Guid Id { get; set; } = Guid.NewGuid(); }
public sealed class Project : Entity
{
    public string Name { get; set; } = "";
    public string AbsolutePath { get; set; } = "";
    public string? Technology { get; set; }
    public ProjectKind Kind { get; set; }
    public string? OpenCodeProjectId { get; set; }
    public Guid? ParentProjectId { get; set; }
    public Project? Parent { get; set; }
    public List<Project> Children { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class AgentSession : Entity
{
    public string OpenCodeSessionId { get; set; } = "";
    public Guid ProjectId { get; set; }
    public Guid? ParentSessionId { get; set; }
    public string Title { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool IsMain { get; set; }
    public bool Hidden { get; set; }
    public bool Managed { get; set; }
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public string? AgentName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAtUtc { get; set; }
}
public sealed class AgentTask : Entity
{
    public Guid SessionId { get; set; }
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public string? AgentName { get; set; }
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Queued;
    public string? CurrentAction { get; set; }
    public string? ResultPreview { get; set; }
    public string? LastError { get; set; }
    public string DeduplicationKey { get; set; } = Guid.NewGuid().ToString("N");
    public string LockKey { get; set; } = "";
    public int ObservedToolCalls { get; set; }
    public bool CancelRequested { get; set; }
    public long CreatedByTelegramUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public List<TaskAttempt> Attempts { get; set; } = [];
}
public sealed class TaskAttempt : Entity
{
    public Guid TaskId { get; set; }
    public int AttemptNumber { get; set; }
    public string? OpenCodeMessageId { get; set; }
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public AttemptStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
public sealed class TaskEvent : Entity
{
    public Guid AttemptId { get; set; }
    public string? OpenCodeEventId { get; set; }
    public string EventType { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class TelegramMessage : Entity
{
    public Guid TaskId { get; set; }
    public long ChatId { get; set; }
    public int MessageId { get; set; }
    public string Purpose { get; set; } = "status";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? LastContentHash { get; set; }
    public DateTime? LastUpdatedAtUtc { get; set; }
}
public sealed class PermissionRequest : Entity
{
    public Guid TaskId { get; set; }
    public string OpenCodeRequestId { get; set; } = "";
    public string OpenCodeSessionId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Resource { get; set; } = "";
    public ApprovalStatus Status { get; set; }
    public long? ApprovedByTelegramUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
public sealed class NavigationState : Entity
{
    public long UserId { get; set; }
    public Guid? SelectedSessionId { get; set; }
    public Guid? SelectedProjectId { get; set; }
    public string? DefaultProviderId { get; set; }
    public string? DefaultModelId { get; set; }
    public string? DefaultAgent { get; set; }
    public string? InputMode { get; set; }
    public string? DraftProviderId { get; set; }
    public string? DraftModelId { get; set; }
    public string? DraftAgent { get; set; }
    public string? FileSearchQuery { get; set; }
    public string? SelectedFilesJson { get; set; }
    public string? PendingFileIdsJson { get; set; }
    public string? BrowserPath { get; set; }
    public int? BrowserMessageId { get; set; }
    public bool ShowHiddenDirectories { get; set; }
    public long TelegramOffset { get; set; }
    public string? PendingAttachmentText { get; set; }
}
public static class TaskStates
{
    public static bool IsActive(AgentTaskStatus s) => s is AgentTaskStatus.Dispatching or AgentTaskStatus.Running or AgentTaskStatus.AwaitingApproval or AgentTaskStatus.NeedsReconciliation;
    public static bool IsTerminal(AgentTaskStatus s) => s is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled;
    public static bool CanTransition(AgentTaskStatus from, AgentTaskStatus to) => from == to || (from, to) switch
    {
        (AgentTaskStatus.Queued, AgentTaskStatus.Dispatching or AgentTaskStatus.Cancelled) => true,
        (AgentTaskStatus.Dispatching, AgentTaskStatus.Running or AgentTaskStatus.Failed or AgentTaskStatus.NeedsReconciliation) => true,
        (AgentTaskStatus.Running or AgentTaskStatus.AwaitingApproval or AgentTaskStatus.NeedsReconciliation,
         AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled or AgentTaskStatus.AwaitingApproval or AgentTaskStatus.Running or AgentTaskStatus.NeedsReconciliation) => true,
        _ => false
    };
}

public sealed class RegisteredRoot : Entity
{
    public string Name { get; set; } = "";
    public string CanonicalPath { get; set; } = "";
}
public sealed class DirectoryBookmark : Entity
{
    public long OwnerUserId { get; set; }
    public string CanonicalPath { get; set; } = "";
    public bool IsFavorite { get; set; }
    public DateTime LastOpenedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FileAttachment : Entity
{
    public string OriginalName { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string MimeType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string? TelegramFileId { get; set; }
    public Guid? TaskId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(7);
    public int? Width { get; set; }
    public int? Height { get; set; }
}
public sealed class FileTransfer : Entity
{
    public Guid? AttachmentId { get; set; }
    public string Direction { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
    public string? LocalPath { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
public sealed class TaskFile : Entity
{
    public Guid TaskId { get; set; }
    public Guid AttachmentId { get; set; }
}
