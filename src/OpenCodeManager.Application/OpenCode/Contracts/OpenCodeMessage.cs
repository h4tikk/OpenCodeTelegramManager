namespace OpenCodeManager.Application;
public sealed record OpenCodeMessage(string Id, string Type, MessageTime Time, string? Text, string? Outcome, List<MessageContent>? Content, MessageSnapshot? Snapshot, StructuredError? Error);
