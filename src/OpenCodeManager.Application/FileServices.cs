using OpenCodeManager.Domain;
namespace OpenCodeManager.Application;

public sealed record LocalFileInfo(string Name, string CanonicalPath, long Size, string MimeType, int? Width = null, int? Height = null);
public sealed record FileListing(List<LocalFileInfo> Files, int Page, bool HasMore);
public interface IFileManager
{
    Task<FileListing> ListAsync(string directory, int page, string? search, long owner, CancellationToken ct);
    Task<LocalFileInfo> InspectAsync(string path, long owner, CancellationToken ct);
    Task<Stream> OpenDownloadAsync(string path, long owner, CancellationToken ct);
    Task CreateDirectoryAsync(string parent, string name, long owner, CancellationToken ct);
    Task<FileAttachment> RegisterUploadAsync(string tempPath, string originalName, string? telegramFileId, long owner, CancellationToken ct);
    string NewUploadPath(string name, long owner);
    Task SaveUploadAsync(Guid attachmentId, string directory, bool overwrite, long owner, CancellationToken ct);
    Task DeleteUploadAsync(Guid attachmentId, long owner, CancellationToken ct);
    Task<FileAttachment> ImportProjectFileAsync(string path, long owner, CancellationToken ct);
    Task<string> CreateArchiveAsync(string? directory, IReadOnlyList<string>? files, long owner, CancellationToken ct);
    Task<List<OpenCodeFileInput>> PrepareTaskFilesAsync(Guid taskId, string directory, string? provider, string? model, CancellationToken ct);
    Task AddToNextTaskAsync(IEnumerable<Guid> attachments, long owner, CancellationToken ct);
    Task CleanupAsync(CancellationToken ct);
}

public sealed class AttachmentPreparationException(string message) : Exception(message);
