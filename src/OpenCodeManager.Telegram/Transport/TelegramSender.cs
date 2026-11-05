using System.Text;
using OpenCodeManager.Application;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace OpenCodeManager.Telegram;
public sealed class TelegramSender(ITelegramBotClient bot, BotSecurityOptions security, OwnerGuard guard, SecretRedactor redactor)
{
    public SemaphoreSlim Gate { get; } = new(1);
    private SemaphoreSlim outgoing => Gate;
    private long Chat => security.OwnerChatId > 0 ? security.OwnerChatId : security.OwnerUserId;

    public async Task SendTextAsync(string text, InlineKeyboardMarkup? keyboard, CancellationToken ct)
    {
        text = redactor.Redact(text);
        if (string.IsNullOrWhiteSpace(text))
            text = "Нет данных.";
        await outgoing.WaitAsync(ct);
        try
        {
            if (text.Length <= 3900)
                await bot.SendMessage(Chat, text, replyMarkup: keyboard, cancellationToken: ct);
            else
            {
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                await bot.SendDocument(Chat, InputFile.FromStream(stream, "result.txt"), caption: "Полный результат", replyMarkup: keyboard, cancellationToken: ct);
            }

            await Task.Delay(1100, ct);
        }
        finally
        {
            outgoing.Release();
        }
    }

    public async Task SendStreamAsync(Stream stream, string name, string mime, bool preview, CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        await outgoing.WaitAsync(ct);
        try
        {
            var file = InputFile.FromStream(stream, name);
            if (preview && mime == "image/gif")
                await bot.SendAnimation(Chat, file, caption: name, cancellationToken: ct);
            else if (preview && mime is "image/png" or "image/jpeg" or "image/webp")
                await bot.SendPhoto(Chat, file, caption: name, cancellationToken: ct);
            else
                await bot.SendDocument(Chat, file, caption: name, cancellationToken: ct);
            await Task.Delay(1100, ct);
        }
        finally
        {
            outgoing.Release();
        }
    }
}
