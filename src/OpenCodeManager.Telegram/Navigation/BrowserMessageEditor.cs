using OpenCodeManager.Application;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace OpenCodeManager.Telegram;
public sealed class BrowserMessageEditor(ITelegramBotClient bot, NavigationService navigation, BotSecurityOptions security, SecretRedactor redactor, TelegramSender sender)
{
    private SemaphoreSlim outgoing => sender.Gate;
    private long Chat => security.OwnerChatId > 0 ? security.OwnerChatId : security.OwnerUserId;

    public async Task RenderBrowserAsync(string text, InlineKeyboardMarkup keyboard, int? messageId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        var id = messageId ?? nav.BrowserMessageId;
        text = redactor.Redact(text);
        if (text.Length > 3900)
            text = text[..3900];
        await outgoing.WaitAsync(ct);
        try
        {
            if (id is int existing)
            {
                try
                {
                    await bot.EditMessageText(Chat, existing, text, replyMarkup: keyboard, cancellationToken: ct);
                }
                catch (ApiRequestException e)when (e.ErrorCode == 400 && e.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                {
                }
                catch (ApiRequestException e)when (e.ErrorCode == 400 && (e.Message.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("message can't be edited", StringComparison.OrdinalIgnoreCase)))
                {
                    id = null;
                }
            }

            if (id is null)
                id = (await bot.SendMessage(Chat, text, replyMarkup: keyboard, cancellationToken: ct)).Id;
            nav.BrowserMessageId = id;
            await navigation.SaveNavigationAsync(nav, ct);
            await Task.Delay(1100, ct);
        }
        finally
        {
            outgoing.Release();
        }
    }
}
