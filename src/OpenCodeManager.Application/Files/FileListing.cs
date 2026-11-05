namespace OpenCodeManager.Application;
public sealed record FileListing(List<LocalFileInfo> Files, int Page, bool HasMore);
