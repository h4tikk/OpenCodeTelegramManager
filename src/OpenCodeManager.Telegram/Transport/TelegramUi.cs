using OpenCodeManager.Application;
using Telegram.Bot;

namespace OpenCodeManager.Telegram;
public sealed record TelegramUi(ITelegramBotClient Bot, TelegramOptions Options, BotSecurityOptions Security, OwnerGuard Guard, CallbackRegistry Callbacks, TelegramSender Sender, BrowserMessageEditor Editor);
