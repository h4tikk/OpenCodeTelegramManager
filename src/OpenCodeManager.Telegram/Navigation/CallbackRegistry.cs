using System.Collections.Concurrent;

namespace OpenCodeManager.Telegram;
public sealed class CallbackRegistry
{
    private readonly ConcurrentDictionary<string, CallbackChoice> choices = new();
    public string Store(string kind, string value, string? provider, Guid? session)
    {
        foreach (var stale in choices.Where(x => x.Value.Expires < DateTime.UtcNow).Select(x => x.Key))
            choices.TryRemove(stale, out _);
        var key = Guid.NewGuid().ToString("N")[..16];
        choices[key] = new CallbackChoice(kind, value, provider, DateTime.UtcNow.AddMinutes(20), session);
        return key;
    }

    public bool TryGetValue(string key, out CallbackChoice choice) => choices.TryGetValue(key, out choice!);
    public bool TryRemove(string key, out CallbackChoice choice) => choices.TryRemove(key, out choice!);
}
