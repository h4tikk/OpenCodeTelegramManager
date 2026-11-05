using System.Text.Json.Serialization;

namespace OpenCodeManager.Application;
public sealed record OpenCodeInbox(string Id, [property: JsonPropertyName("sessionID")] string SessionId, string Type);
