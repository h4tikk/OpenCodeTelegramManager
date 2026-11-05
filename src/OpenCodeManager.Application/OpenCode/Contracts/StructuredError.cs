namespace OpenCodeManager.Application;
public sealed record StructuredError(string Type, string Message, int? Status);
