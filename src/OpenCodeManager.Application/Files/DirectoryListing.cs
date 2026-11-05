namespace OpenCodeManager.Application;
public sealed record DirectoryListing(string CanonicalPath, string? ParentPath, List<DirectoryEntry> Directories, int Page, bool HasMore, string? Error = null);
