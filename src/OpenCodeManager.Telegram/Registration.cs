using Microsoft.Extensions.DependencyInjection;

namespace OpenCodeManager.Telegram;
public static class TelegramRegistration
{
    public static IServiceCollection AddManagerTelegram(this IServiceCollection services)
    {
        services.AddSingleton<TelegramUi>();
        services.AddSingleton<CallbackRegistry>();
        services.AddSingleton<TelegramSender>();
        services.AddSingleton<BrowserMessageEditor>();
        services.AddSingleton<TelegramAuthorization>();
        services.AddSingleton<TelegramCallbackRouter>();
        services.AddSingleton<TelegramCommandRouter>();
        services.AddSingleton<TelegramConversationHandler>();
        services.AddSingleton<TelegramUpdateHandler>();
        services.AddSingleton<FileTransferService>();
        services.AddSingleton<TaskStatusPublisher>();
        services.AddSingleton<PermissionNotificationPublisher>();
        services.AddSingleton<MainMenuHandler>();
        services.AddSingleton<ProjectMenuHandler>();
        services.AddSingleton<SessionMenuHandler>();
        services.AddSingleton<CatalogMenuHandler>();
        services.AddSingleton<TaskMenuHandler>();
        services.AddSingleton<SettingsMenuHandler>();
        services.AddSingleton<DirectoryBrowserHandler>();
        services.AddSingleton<FileMenuHandler>();
        services.AddSingleton<UploadHandler>();
        services.AddSingleton<TaskResultFilesHandler>();
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<MainMenuHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<ProjectMenuHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<SessionMenuHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<CatalogMenuHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<TaskMenuHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<SettingsMenuHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<DirectoryBrowserHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<FileMenuHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<UploadHandler>());
        services.AddSingleton<ITelegramCallbackHandler>(p => p.GetRequiredService<TaskResultFilesHandler>());
        services.AddHostedService<TelegramPollingService>();
        services.AddHostedService<TelegramStatusService>();
        return services;
    }
}
