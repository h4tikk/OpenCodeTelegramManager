namespace OpenCodeManager.Domain;
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
