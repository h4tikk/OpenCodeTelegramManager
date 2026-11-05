using OpenCodeManager.Application;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OpenCodeManager.Telegram;
public sealed class TelegramAuthorization(OwnerGuard guard)
{
    public bool IsAuthorized(Update u) => u.CallbackQuery is { } c ? c.Message is { } m && guard.IsAuthorized(c.From.Id, m.Chat.Id, m.Chat.Type == ChatType.Private) : u.Message is { From: { } from } msg && guard.IsAuthorized(from.Id, msg.Chat.Id, msg.Chat.Type == ChatType.Private);
}
