namespace OpenCodeManager.Application;
public sealed class TelegramOptions
{
    public string Token { get; set; } = "";
    public int StatusIntervalSeconds { get; set; } = 5;
    public long MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;
}
