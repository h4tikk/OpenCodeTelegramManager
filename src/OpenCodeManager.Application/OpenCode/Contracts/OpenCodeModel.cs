using System.Text.Json.Serialization;

namespace OpenCodeManager.Application;
public sealed record OpenCodeModel(string Id, [property: JsonPropertyName("providerID")] string ProviderId, string Name, bool Enabled, string Status, ModelCapabilities? Capabilities = null);
