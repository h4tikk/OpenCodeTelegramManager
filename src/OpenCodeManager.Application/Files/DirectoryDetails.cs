namespace OpenCodeManager.Application;
public sealed record DirectoryDetails(string CanonicalPath, string Technology, bool IsGitRepository, Guid ProjectId, int Sessions);
