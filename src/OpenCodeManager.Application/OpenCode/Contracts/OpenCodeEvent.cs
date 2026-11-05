using System.Text.Json;

namespace OpenCodeManager.Application;
public sealed record OpenCodeEvent(string? Id, string Type, JsonElement Data);
