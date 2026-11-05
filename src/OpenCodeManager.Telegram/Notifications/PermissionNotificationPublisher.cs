using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using static OpenCodeManager.Telegram.TelegramKeyboards;

namespace OpenCodeManager.Telegram;
public sealed class PermissionNotificationPublisher(TelegramUi ui, IManagerDbFactory factory, SecretRedactor redactor, TelegramSender sender, PermissionManager permissions) : TelegramHandler(ui)
{
    private SemaphoreSlim outgoing => sender.Gate;

    public async Task PublishAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var pending = await db.Permissions.Where(x => x.Status == ApprovalStatus.Pending).ToListAsync(ct);
        foreach (var r in pending)
        {
            var purpose = "permission:" + r.Id.ToString("N");
            if (await db.TelegramMessages.AnyAsync(x => x.TaskId == r.TaskId && x.ChatId == Chat && x.Purpose == purpose, ct))
                continue;
            await outgoing.WaitAsync(ct);
            try
            {
                var buttons = new List<InlineKeyboardButton[]>
                {
                    new[]
                    {
                        B("Разрешить один раз", "approve", r.Id.ToString("N")),
                        B("Отклонить", "reject", r.Id.ToString("N"))
                    }
                };
                var task = await db.Tasks.SingleAsync(x => x.Id == r.TaskId, ct);
                var s = await db.Sessions.SingleAsync(x => x.Id == task.SessionId, ct);
                if (permissions.CanAutoApprove(r.Action, r.Resource.Split('\n'), s.WorkingDirectory))
                    buttons.Add([B("Точное правило сессии", "rule", r.Id.ToString("N"))]);
                var sent = await bot.SendMessage(Chat, redactor.Redact("Требуется разрешение\n" + r.Action + "\n" + r.Resource)[..Math.Min(3500, redactor.Redact("Требуется разрешение\n" + r.Action + "\n" + r.Resource).Length)], replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                db.TelegramMessages.Add(new() { TaskId = r.TaskId, ChatId = Chat, MessageId = sent.Id, Purpose = purpose });
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
