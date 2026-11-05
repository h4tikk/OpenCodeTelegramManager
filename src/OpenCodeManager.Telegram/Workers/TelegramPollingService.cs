using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Application;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using static OpenCodeManager.Telegram.TelegramErrors;

namespace OpenCodeManager.Telegram;
public sealed class TelegramPollingService(ITelegramBotClient bot, NavigationService navigation, TelegramUpdateHandler handler, TelegramAuthorization authorization, TelegramSender sender, TelegramOptions options, BotSecurityOptions security, ILogger<TelegramPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (security.OwnerUserId <= 0 || string.IsNullOrWhiteSpace(options.Token))
        {
            logger.LogError("Telegram disabled: Telegram__Token and BotSecurity__OwnerUserId are required");
            return;
        }

        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
                    var updates = await bot.GetUpdates(offset: checked((int)nav.TelegramOffset), limit: 30, timeout: 25, allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery], cancellationToken: ct);
                    foreach (var update in updates)
                    {
                        try
                        {
                            await handler.HandleAsync(update, ct);
                        }
                        catch (OperationCanceledException)when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning("Telegram update {UpdateId} failed: {ErrorType}", update.Id, e.GetType().Name);
                            if (authorization.IsAuthorized(update))
                                await sender.SendTextAsync("Действие не выполнено: " + SafeError(e), null, ct);
                        }

                        nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
                        nav.TelegramOffset = update.Id + 1;
                        await navigation.SaveNavigationAsync(nav, ct);
                    }
                }
                catch (OperationCanceledException)when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ApiRequestException e)
                {
                    logger.LogWarning("Telegram API error {Code}", e.ErrorCode);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(e.Parameters?.RetryAfter ?? 5, 1, 60)), ct);
                }
                catch (Exception e)
                {
                    logger.LogWarning("Telegram polling disconnected: {ErrorType}", e.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
        }
    }
}
