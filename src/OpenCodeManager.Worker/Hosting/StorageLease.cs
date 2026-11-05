using OpenCodeManager.Application;

namespace OpenCodeManager.Worker;
public sealed class StorageLease : IDisposable
{
    private readonly FileStream stream;
    private StorageLease(FileStream stream) => this.stream = stream;
    public static StorageLease? Acquire(StorageOptions options, bool exclusive)
    {
        var directory = Path.GetDirectoryName(options.DatabasePath)!;
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            return exclusive ? new(new FileStream(Path.Combine(directory, "manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) : null;
        }
        catch (IOException error)
        {
            throw new ManagerAlreadyRunningException(error);
        }
    }

    public void Dispose() => stream.Dispose();
}
