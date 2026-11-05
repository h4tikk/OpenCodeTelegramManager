using Microsoft.Extensions.Configuration;
using OpenCodeManager.Application;

namespace OpenCodeManager.Worker;
public sealed record WorkerConfiguration(TelegramOptions Telegram, BotSecurityOptions Security, OpenCodeOptions OpenCode, TaskExecutionOptions Execution, StorageOptions Storage, ProjectOptions Projects, FileStorageOptions Files, AutonomyOptions Autonomy)
{
    public bool TelegramConfigured => Security.OwnerUserId > 0 && !string.IsNullOrWhiteSpace(Telegram.Token);

    public static WorkerConfiguration Load(ConfigurationManager configuration)
    {
        configuration.AddProtectedEnvironmentFile();
        T Get<T>(string section)
            where T : class, new() => configuration.GetSection(section).Get<T>() ?? new();
        return new(Get<TelegramOptions>("Telegram"), Get<BotSecurityOptions>("BotSecurity"), Get<OpenCodeOptions>("OpenCode"), Get<TaskExecutionOptions>("TaskExecution"), Get<StorageOptions>("Storage"), Get<ProjectOptions>("Projects"), Get<FileStorageOptions>("Files"), Get<AutonomyOptions>("Autonomy"));
    }

    public void Validate()
    {
        if (!Uri.TryCreate(OpenCode.BaseUrl, UriKind.Absolute, out var endpoint) || (!endpoint.IsLoopback && endpoint.Scheme != "https") || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0)
            throw new InvalidOperationException("Invalid OpenCode endpoint");
        if (Execution.MaxConcurrentTasks is < 1 or > 16 || Execution.PollSeconds is < 1 or > 60 || Execution.MaxDurationMinutes is < 1 or > 1440 || Execution.MaxToolCalls < 1)
            throw new InvalidOperationException("Invalid task execution limits");
        Storage.DatabasePath = Path.GetFullPath(Storage.DatabasePath);
    }
}
