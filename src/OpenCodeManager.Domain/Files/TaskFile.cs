namespace OpenCodeManager.Domain;
public sealed class TaskFile : Entity
{
    public Guid TaskId { get; set; }
    public Guid AttachmentId { get; set; }
}
