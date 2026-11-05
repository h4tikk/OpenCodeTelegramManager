using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;

public interface IManagerDb : IAsyncDisposable
{
    DbSet<Project> Projects { get; }
    DbSet<AgentSession> Sessions { get; }
    DbSet<AgentTask> Tasks { get; }
    DbSet<TaskAttempt> Attempts { get; }
    DbSet<TaskEvent> Events { get; }
    DbSet<TelegramMessage> TelegramMessages { get; }
    DbSet<PermissionRequest> Permissions { get; }
    DbSet<NavigationState> Navigation { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
public interface IManagerDbFactory { Task<IManagerDb> CreateAsync(CancellationToken ct = default); }
public sealed record ApiData<T>(T Data);
public sealed record ApiCursor(string? Previous, string? Next);
public sealed record ApiPage<T>(List<T> Data, ApiCursor? Cursor);
public sealed record ModelRef(string Id, [property: JsonPropertyName("providerID")] string ProviderId, string? Variant = null);
public sealed record OpenCodeModel(string Id, [property: JsonPropertyName("providerID")] string ProviderId, string Name, bool Enabled, string Status);
public sealed record OpenCodeAgent(string Id, string Name, string Mode, bool Hidden);
public sealed record OpenCodeProvider(string Id, string Name);
public sealed record OpenCodeProject(string Id, string Canonical, string? Name);
public sealed record LocationRef(string? Directory);
public sealed record SessionTime(double Created, double Updated, double? Busy, double? Idle, double? Archived);
public sealed record PermissionRule(string Action, string Resource, string Effect);
public sealed record OpenCodeSession(string Id, [property: JsonPropertyName("projectID")] string ProjectId, string? Title, LocationRef Location, SessionTime Time, string? Agent, ModelRef? Model, string? Outcome, List<PermissionRule>? Permissions);
public sealed record CreateSessionRequest(string Id, string Title, LocationRef Location, List<PermissionRule> Permissions);
public sealed record PromptRequest(string Id, string Text, Dictionary<string,string> Metadata);
public sealed record OpenCodeInbox(string Id, [property: JsonPropertyName("sessionID")] string SessionId, string Type);
public sealed record MessageTime(double Created, double? Completed);
public sealed record MessageContent(string Type, string? Text, string? Name);
public sealed record MessageSnapshot(List<string>? Files);
public sealed record StructuredError(string Type, string Message, int? Status);
public sealed record OpenCodeMessage(string Id, string Type, MessageTime Time, string? Text, string? Outcome, List<MessageContent>? Content, MessageSnapshot? Snapshot, StructuredError? Error);
public sealed record OpenCodePermission(string Id, [property: JsonPropertyName("sessionID")] string SessionId, string Action, List<string> Resources, List<string>? Save);
public sealed record OpenCodeDiff(string File, string Patch, int Additions, int Deletions, string Status);
public sealed record ServerInfo(string Version, int Pid, List<string> Urls);
public sealed record OpenCodeEvent(string? Id, string Type, JsonElement Data);
public sealed class OpenCodeApiException(int status, string operation) : Exception($"OpenCode {operation}: HTTP {status}")
{
    public int StatusCode { get; } = status;
    public string Code => StatusCode switch { 401 => "Authentication", 403 => "Forbidden", 404 => "NotFound", 409 => "Conflict", 429 => "RateLimit", >= 500 => "Server", _ => "Request" };
}
public interface IOpenCodeClient
{
    Task<ServerInfo> GetInfoAsync(CancellationToken ct);
    Task<List<OpenCodeProject>> GetProjectsAsync(CancellationToken ct);
    Task<List<OpenCodeModel>> GetModelsAsync(string? directory, CancellationToken ct);
    Task<List<OpenCodeAgent>> GetAgentsAsync(string? directory, CancellationToken ct);
    Task<List<OpenCodeProvider>> GetProvidersAsync(CancellationToken ct);
    Task<ApiPage<OpenCodeSession>> GetSessionsAsync(string? cursor, CancellationToken ct);
    Task<OpenCodeSession> GetSessionAsync(string id, CancellationToken ct);
    Task<OpenCodeSession> CreateSessionAsync(CreateSessionRequest request, CancellationToken ct);
    Task UpdateSessionAsync(string id, string? title, bool? archived, List<PermissionRule>? permissions, CancellationToken ct);
    Task SwitchModelAsync(string id, ModelRef model, CancellationToken ct);
    Task SwitchAgentAsync(string id, string agent, CancellationToken ct);
    Task<OpenCodeInbox> SendPromptAsync(string sessionId, PromptRequest prompt, CancellationToken ct);
    Task InterruptAsync(string sessionId, CancellationToken ct);
    Task<ApiPage<OpenCodeMessage>> GetMessagesAsync(string sessionId, string? cursor, CancellationToken ct);
    Task<List<OpenCodeInbox>> GetInboxAsync(string sessionId, CancellationToken ct);
    Task<List<OpenCodePermission>> GetPermissionsAsync(string sessionId, CancellationToken ct);
    Task ReplyPermissionAsync(string sessionId, string requestId, string decision, CancellationToken ct);
    Task<List<OpenCodeDiff>> GetDiffAsync(string sessionId, string? messageId, CancellationToken ct);
    IAsyncEnumerable<OpenCodeEvent> SubscribeAsync(CancellationToken ct);
}
public interface IPathPolicy
{
    string Canonicalize(string path);
    string DemandAllowed(string path);
    string LockKey(string path);
    IEnumerable<(string Path, string? Parent, ProjectKind Kind)> Discover();
}
public interface IManagerNotifications { Task TaskChangedAsync(Guid taskId, CancellationToken ct); }
