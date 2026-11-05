using System.Text.Json.Serialization;

namespace OpenCodeManager.Application;
public sealed record OpenCodePermission(string Id, [property: JsonPropertyName("sessionID")] string SessionId, string Action, List<string> Resources, List<string>? Save);
