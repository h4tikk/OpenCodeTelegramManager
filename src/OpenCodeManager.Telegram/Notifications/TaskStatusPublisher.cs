using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using static OpenCodeManager.Telegram.TaskMessageFormatter;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class TaskStatusPublisher(TelegramUi ui, IManagerDbFactory factory, SecretRedactor redactor, TelegramSender sender) : TelegramHandler(ui)
{
    private SemaphoreSlim outgoing => sender.Gate;

    public async Task PublishAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var tasks = await db.Tasks.OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
        foreach (var t in tasks)
        {
            var msg = await db.TelegramMessages.SingleOrDefaultAsync(x => x.TaskId == t.Id && x.ChatId == Chat && x.Purpose == "status", ct);
            var session = await db.Sessions.SingleAsync(x => x.Id == t.SessionId, ct);
            var text = redactor.Redact(TaskText(t, session));
            if (text.Length > 3900)
                text = text[..3900] + "\n… Полный результат: кнопка ниже.";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            if (msg?.LastContentHash == hash)
                continue;
            await outgoing.WaitAsync(ct);
            try
            {
                if (msg is null)
                {
                    var sent = await bot.SendMessage(Chat, text, replyMarkup: TaskButtons(t), cancellationToken: ct);
                    msg = new TelegramMessage
                    {
                        TaskId = t.Id,
                        ChatId = Chat,
                        MessageId = sent.Id
                    };
                    db.TelegramMessages.Add(msg);
                }
                else
                {
                    try
                    {
                        await bot.EditMessageText(Chat, msg.MessageId, text, replyMarkup: TaskButtons(t), cancellationToken: ct);
                    }
                    catch (ApiRequestException e)when (e.ErrorCode == 400 && e.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                    {
                    }
                }

                msg.LastContentHash = hash;
                msg.LastUpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await Task.Delay(1100, ct);
            }
            finally
            {
                outgoing.Release();
            }
        }
    }
}
