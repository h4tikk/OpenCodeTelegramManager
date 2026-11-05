using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class FileStoragePaths(FileStorageOptions options)
{
    public string Get(string kind)
    {
        var path = Path.Combine(options.DataDirectory, kind);
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
