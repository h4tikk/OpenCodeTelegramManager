using Microsoft.Extensions.DependencyInjection;
using OpenCodeManager.Application;
using OpenCodeManager.Infrastructure;
using OpenCodeManager.Telegram;
using Telegram.Bot;

namespace OpenCodeManager.Worker;
public static class WorkerRegistration
{
    public static IServiceCollection AddManagerWorker(this IServiceCollection services, WorkerConfiguration configuration, bool run)
    {
        foreach (var options in new object[]
        {
            configuration.Telegram,
            configuration.Security,
            configuration.OpenCode,
            configuration.Execution,
            configuration.Storage,
            configuration.Projects,
            configuration.Files,
            configuration.Autonomy
        })
            services.AddSingleton(options.GetType(), options);
        services.AddSingleton(configuration);
        services.AddSingleton<OwnerGuard>();
        services.AddManagerInfrastructure(configuration.Storage, configuration.OpenCode);
        services.AddSingleton<WorkerCommands>();
        services.AddSingleton<DiagnosticCommand>();
        if (run)
        {
            services.AddHostedService<HealthWorker>();
            if (configuration.TelegramConfigured)
            {
                services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(configuration.Telegram.Token));
                services.AddHostedService<DispatcherWorker>();
                services.AddHostedService<OpenCodeEventListener>();
                services.AddManagerTelegram();
            }
        }

        return services;
    }
}
