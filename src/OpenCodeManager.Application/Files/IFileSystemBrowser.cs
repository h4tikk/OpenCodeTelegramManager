namespace OpenCodeManager.Application;
public interface IFileSystemBrowser
{
    Task<DirectoryListing> GetDirectoriesAsync(string path, int page = 0, bool showHidden = false, CancellationToken cancellationToken = default);
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<DirectoryDetails> OpenAsync(string path, long owner, CancellationToken ct);
    Task<string> GitStatusAsync(string path, CancellationToken ct);
}
