namespace OpenCodeManager.Telegram;
public sealed record CallbackChoice(string Kind, string Value, string? Provider, DateTime Expires, Guid? SessionId);
