namespace OpenCodeManager.Domain;
public sealed class DirectoryBookmark : Entity
{
    public long OwnerUserId { get; set; }
    public string CanonicalPath { get; set; } = "";
    public bool IsFavorite { get; set; }
    public DateTime LastOpenedAtUtc { get; set; } = DateTime.UtcNow;
}
