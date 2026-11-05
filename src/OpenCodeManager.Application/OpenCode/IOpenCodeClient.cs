namespace OpenCodeManager.Application;
public interface IOpenCodeClient
{
    Task<ServerInfo> GetInfoAsync(CancellationToken ct);
    Task<List<OpenCodeProject>> GetProjectsAsync(CancellationToken ct);
    Task<List<OpenCodeModel>> GetModelsAsync(string? directory, CancellationToken ct);
    Task<List<OpenCodeAgent>> GetAgentsAsync(string? directory, CancellationToken ct);
    Task<List<OpenCodeProvider>> GetProvidersAsync(CancellationToken ct);
    Task<ApiPage<OpenCodeSession>> GetSessionsAsync(string? cursor, CancellationToken ct);
    Task<ApiPage<OpenCodeSession>> GetDirectorySessionsAsync(string directory, string? cursor, CancellationToken ct);
    Task<OpenCodeSession> GetSessionAsync(string id, CancellationToken ct);
    Task<OpenCodeSession> CreateSessionAsync(CreateSessionRequest request, CancellationToken ct);
    Task UpdateSessionAsync(string id, string? title, bool? archived, List<PermissionRule>? permissions, CancellationToken ct);
    Task SwitchModelAsync(string id, ModelRef model, CancellationToken ct);
    Task SwitchAgentAsync(string id, string agent, CancellationToken ct);
    Task<OpenCodeInbox> SendPromptAsync(string sessionId, PromptRequest prompt, CancellationToken ct);
    Task InterruptAsync(string sessionId, CancellationToken ct);
    Task CancelInboxAsync(string sessionId, string messageId, CancellationToken ct);
    Task<ApiPage<OpenCodeMessage>> GetMessagesAsync(string sessionId, string? cursor, CancellationToken ct);
    Task<List<OpenCodeInbox>> GetInboxAsync(string sessionId, CancellationToken ct);
    Task<List<OpenCodePermission>> GetPermissionsAsync(string sessionId, CancellationToken ct);
    Task ReplyPermissionAsync(string sessionId, string requestId, string decision, CancellationToken ct);
    Task<List<OpenCodeDiff>> GetDiffAsync(string sessionId, string? messageId, CancellationToken ct);
    IAsyncEnumerable<OpenCodeEvent> SubscribeAsync(CancellationToken ct);
}
