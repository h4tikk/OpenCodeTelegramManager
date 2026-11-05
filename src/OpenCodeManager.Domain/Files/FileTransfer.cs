namespace OpenCodeManager.Domain;
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
