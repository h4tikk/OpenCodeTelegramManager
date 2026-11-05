namespace OpenCodeManager.Domain;
public sealed class TaskEvent : Entity
{
    public Guid AttemptId { get; set; }
    public string? OpenCodeEventId { get; set; }
    public string EventType { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
