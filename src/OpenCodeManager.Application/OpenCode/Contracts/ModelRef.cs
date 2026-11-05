using System.Text.Json.Serialization;

namespace OpenCodeManager.Application;
public sealed record ModelRef(string Id, [property: JsonPropertyName("providerID")] string ProviderId, string? Variant = null);
