namespace OpenCodeManager.Telegram;
public interface ITelegramCallbackHandler
{
    IReadOnlySet<string> Actions { get; }

    Task<bool> HandleCallbackAsync(string action, string argument, int messageId, CancellationToken ct);
}
