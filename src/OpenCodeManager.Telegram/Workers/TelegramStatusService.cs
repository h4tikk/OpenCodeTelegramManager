using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Application;
using Telegram.Bot.Exceptions;

namespace OpenCodeManager.Telegram;
public sealed class TelegramStatusService(TaskStatusPublisher statuses, PermissionNotificationPublisher permissions, TelegramOptions options, ILogger<TelegramStatusService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await statuses.PublishAsync(ct);
                await permissions.PublishAsync(ct);
            }
            catch (OperationCanceledException)when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ApiRequestException e)
            {
                logger.LogWarning("Telegram status update failed: {Code}", e.ErrorCode);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(e.Parameters?.RetryAfter ?? 5, 1, 60)), ct);
            }
            catch (Exception e)
            {
                logger.LogWarning("Telegram status update failed: {ErrorType}", e.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, options.StatusIntervalSeconds)), ct);
        }
    }
}
