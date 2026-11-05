using OpenCodeManager.Application;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace OpenCodeManager.Telegram;
public abstract class TelegramHandler(TelegramUi ui)
{
    protected ITelegramBotClient bot => ui.Bot;
    protected TelegramOptions options => ui.Options;
    protected BotSecurityOptions security => ui.Security;
    protected OwnerGuard guard => ui.Guard;
    protected CallbackRegistry choices => ui.Callbacks;
    protected long Chat => security.OwnerChatId > 0 ? security.OwnerChatId : security.OwnerUserId;

    protected Task SendTextAsync(string text, InlineKeyboardMarkup? keyboard, CancellationToken ct) => ui.Sender.SendTextAsync(text, keyboard, ct);
    protected Task RenderBrowserAsync(string text, InlineKeyboardMarkup keyboard, int? messageId, CancellationToken ct) => ui.Editor.RenderBrowserAsync(text, keyboard, messageId, ct);
    protected string StoreChoice(string kind, string value, string? provider, Guid? sessionId) => choices.Store(kind, value, provider, sessionId);
}
