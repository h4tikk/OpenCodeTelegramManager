namespace OpenCodeManager.Application;
public interface IAttachmentProcessor
{
    string ValidateName(string name, long size);
    Task<string> ExtractAsync(string path, string extension, CancellationToken ct);
}
