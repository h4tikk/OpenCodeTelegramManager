namespace OpenCodeManager.Domain;
public sealed class NavigationState : Entity
{
    public long UserId { get; set; }
    public Guid? SelectedSessionId { get; set; }
    public Guid? SelectedProjectId { get; set; }
    public string? DefaultProviderId { get; set; }
    public string? DefaultModelId { get; set; }
    public string? DefaultAgent { get; set; }
    public string? InputMode { get; set; }
    public string? DraftProviderId { get; set; }
    public string? DraftModelId { get; set; }
    public string? DraftAgent { get; set; }
    public string? FileSearchQuery { get; set; }
    public string? SelectedFilesJson { get; set; }
    public string? PendingFileIdsJson { get; set; }
    public string? BrowserPath { get; set; }
    public int? BrowserMessageId { get; set; }
    public bool ShowHiddenDirectories { get; set; }
    public long TelegramOffset { get; set; }
    public string? PendingAttachmentText { get; set; }
}
