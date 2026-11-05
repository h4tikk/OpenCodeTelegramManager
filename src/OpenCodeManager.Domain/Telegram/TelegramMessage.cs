namespace OpenCodeManager.Domain;
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
