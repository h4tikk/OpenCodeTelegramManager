namespace OpenCodeManager.Application;
public sealed record ApiPage<T>(List<T> Data, ApiCursor? Cursor);
