namespace OpenCodeManager.Application;

public sealed class TelegramOptions { public string Token { get; set; } = ""; public int StatusIntervalSeconds { get; set; } = 5; public long MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024; }
public sealed class BotSecurityOptions { public long OwnerUserId { get; set; } public long OwnerChatId { get; set; } }
public sealed class OpenCodeOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:49374";
    public string Username { get; set; } = "opencode";
    public string Password { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}
public sealed class TaskExecutionOptions
{
    public int MaxConcurrentTasks { get; set; } = 2;
    public int MaxDurationMinutes { get; set; } = 60;
    public int MaxToolCalls { get; set; } = 200;
    public int MaxVerificationCycles { get; set; } = 8;
    public int PollSeconds { get; set; } = 3;
    public int MaxEventsPerAttempt { get; set; } = 300;
}
public sealed class StorageOptions
{
    public string DatabasePath { get; set; } = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "opencode-manager/manager.db");
}
public sealed class ProjectOptions
{
    public string[] AllowedRoots { get; set; } = new[] { "Projects", "Development", "Code", "Workspace", "University" }.Select(x => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), x)).Where(Directory.Exists).ToArray();
    public int DiscoveryDepth { get; set; } = 3;
    public int MaxDiscoveredDirectories { get; set; } = 1500;
}
public sealed class OwnerGuard(BotSecurityOptions options)
{
    public bool IsAuthorized(long userId, long chatId, bool isPrivate) => options.OwnerUserId > 0 && userId == options.OwnerUserId && isPrivate && chatId == (options.OwnerChatId > 0 ? options.OwnerChatId : options.OwnerUserId);
    public void Demand(long userId) { if (options.OwnerUserId <= 0 || userId != options.OwnerUserId) throw new UnauthorizedAccessException("Owner authorization required"); }
}
public static class CallbackData
{
    public static bool TryParse(string? value, out string action, out string argument)
    {
        action = argument = "";
        if (value is null || value.Length > 64 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ':' or '-'))) return false;
        var parts = value.Split(':');
        if (parts.Length != 2 || parts[0].Length is 0 or > 20 || parts[1].Length is 0 or > 40) return false;
        action = parts[0]; argument = parts[1]; return true;
    }
}
