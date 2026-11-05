namespace OpenCodeManager.Application;
public sealed class StorageOptions
{
    public string DatabasePath { get; set; } = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "opencode-manager/manager.db");
}
