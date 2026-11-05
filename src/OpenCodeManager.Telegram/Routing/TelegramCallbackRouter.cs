using OpenCodeManager.Application;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OpenCodeManager.Telegram;
public sealed class TelegramCallbackRouter(ITelegramBotClient bot, IEnumerable<ITelegramCallbackHandler> handlers)
{
    public async Task RouteAsync(CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        if (!CallbackData.TryParse(callback.Data, out var action, out var argument))
            return;
        var handler = handlers.SingleOrDefault(x => x.Actions.Contains(action));
        if (handler is not null)
            await handler.HandleCallbackAsync(action, argument, callback.Message!.Id, ct);
    }
}
