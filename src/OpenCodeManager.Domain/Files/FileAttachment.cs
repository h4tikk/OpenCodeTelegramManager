namespace OpenCodeManager.Domain;
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
