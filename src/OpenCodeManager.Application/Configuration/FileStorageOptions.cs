namespace OpenCodeManager.Application;
public sealed class FileStorageOptions
{
    public string DataDirectory { get; set; } = Path.GetDirectoryName(new StorageOptions().DatabasePath)!;
    public long MaxDownloadBytes { get; set; } = 49 * 1024 * 1024;
    public long MaxArchiveBytes { get; set; } = 40 * 1024 * 1024;
    public int MaxArchiveFiles { get; set; } = 1000;
    public int MaxArchiveDepth { get; set; } = 12;
    public int MaxArchiveSeconds { get; set; } = 30;
    public int MaxImagePixels { get; set; } = 40_000_000;
}
