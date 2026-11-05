namespace OpenCodeManager.Domain;
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
