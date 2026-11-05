using OpenCodeManager.Application;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class FileManager(LocalFileService local, AttachmentStorageService attachments, ArchiveService archives, TaskAttachmentFactory taskFiles, AttachmentSelectionService selection) : IFileManager
{
    public Task<FileListing> ListAsync(string directory, int page, string? search, long owner, CancellationToken ct) => local.ListAsync(directory, page, search, owner, ct);
    public Task<LocalFileInfo> InspectAsync(string path, long owner, CancellationToken ct) => local.InspectAsync(path, owner, ct);
    public Task<Stream> OpenDownloadAsync(string path, long owner, CancellationToken ct) => local.OpenDownloadAsync(path, owner, ct);
    public Task CreateDirectoryAsync(string parent, string name, long owner, CancellationToken ct) => local.CreateDirectoryAsync(parent, name, owner, ct);
    public string NewUploadPath(string name, long owner) => attachments.NewUploadPath(name, owner);
    public Task<FileAttachment> RegisterUploadAsync(string path, string name, string? telegramFileId, long owner, CancellationToken ct) => attachments.RegisterUploadAsync(path, name, telegramFileId, owner, ct);
    public Task SaveUploadAsync(Guid attachmentId, string directory, bool overwrite, long owner, CancellationToken ct) => attachments.SaveUploadAsync(attachmentId, directory, overwrite, owner, ct);
    public Task DeleteUploadAsync(Guid attachmentId, long owner, CancellationToken ct) => attachments.DeleteUploadAsync(attachmentId, owner, ct);
    public Task<FileAttachment> ImportProjectFileAsync(string path, long owner, CancellationToken ct) => attachments.ImportProjectFileAsync(path, owner, ct);
    public Task<string> CreateArchiveAsync(string? directory, IReadOnlyList<string>? files, long owner, CancellationToken ct) => archives.CreateArchiveAsync(directory, files, owner, ct);
    public Task<List<OpenCodeFileInput>> PrepareTaskFilesAsync(Guid taskId, string directory, string? provider, string? model, CancellationToken ct) => taskFiles.PrepareTaskFilesAsync(taskId, directory, provider, model, ct);
    public Task AddToNextTaskAsync(IEnumerable<Guid> ids, long owner, CancellationToken ct) => selection.AddToNextTaskAsync(ids, owner, ct);
    public Task CleanupAsync(CancellationToken ct) => attachments.CleanupAsync(ct);
}
