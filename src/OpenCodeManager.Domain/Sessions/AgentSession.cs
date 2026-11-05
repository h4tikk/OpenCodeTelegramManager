namespace OpenCodeManager.Domain;
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
