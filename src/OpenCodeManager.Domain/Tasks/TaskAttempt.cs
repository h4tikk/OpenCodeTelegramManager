namespace OpenCodeManager.Domain;
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
