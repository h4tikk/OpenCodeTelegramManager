using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace OpenCodeManager.Telegram;
public sealed class TelegramUpdateHandler(TelegramAuthorization authorization, TelegramCallbackRouter callbacks, TelegramCommandRouter commands, TelegramConversationHandler conversations, UploadHandler uploads, ILogger<TelegramUpdateHandler> logger)
{
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (!authorization.IsAuthorized(update))
            return;
        using var correlation = logger.BeginScope(new Dictionary<string, object> { { "CorrelationId", "telegram:" + update.Id } });
        if (update.CallbackQuery is { } callback)
        {
            await callbacks.RouteAsync(callback, ct);
            return;
        }

        var message = update.Message!;
        if (message.Document is not null || message.Photo is { Length: > 0 })
        {
            await uploads.ReceiveFileAsync(message, update.Id, ct);
            return;
        }

        var input = message.Text?.Trim();
        if (string.IsNullOrEmpty(input))
            return;
        if (input.StartsWith('/'))
            await commands.HandleAsync(input, ct);
        else
            await conversations.HandleAsync(input, update.Id, ct);
    }
}
