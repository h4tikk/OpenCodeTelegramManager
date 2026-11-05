namespace OpenCodeManager.Application;
public sealed record LocalFileInfo(string Name, string CanonicalPath, long Size, string MimeType, int? Width = null, int? Height = null);
