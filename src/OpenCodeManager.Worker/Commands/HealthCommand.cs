using OpenCodeManager.Application;

namespace OpenCodeManager.Worker;
public static class HealthCommand
{
    public static int Check(StorageOptions storage)
    {
        var path = Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!, "worker.heartbeat");
        return File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromSeconds(45) ? 0 : 1;
    }
}
