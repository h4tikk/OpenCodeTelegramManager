using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCodeManager.Application;
public sealed record OpenCodeSession(string Id, [property: JsonPropertyName("projectID")] string ProjectId, string? Title, LocationRef Location, SessionTime Time, string? Agent, ModelRef? Model, string? Outcome, List<PermissionRule>? Permissions, Dictionary<string, JsonElement>? Metadata = null);
