using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class OpenCodeApiClient(HttpClient http, OpenCodeOptions options) : IOpenCodeClient
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        if (!string.IsNullOrEmpty(options.Password))
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Username + ":" + options.Password)));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        return request;
    }

    private async Task<T> Get<T>(string path, CancellationToken ct)
    {
        for (int attempt = 0;; attempt++)
        {
            using var request = Request(HttpMethod.Get, path);
            using var response = await http.SendAsync(request, ct);
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt + 1);
                await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, ct);
                continue;
            }

            Ensure(response, "GET");
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct) ?? throw new JsonException("Empty OpenCode response");
        }
    }

    private static void Ensure(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
            throw new OpenCodeApiException((int)response.StatusCode, operation);
    }

    private async Task<T> Write<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = Request(method, path, body);
        using var response = await http.SendAsync(request, ct);
        Ensure(response, method.Method);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct) ?? throw new JsonException("Empty OpenCode response");
    }

    private async Task Write(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = Request(method, path, body);
        using var response = await http.SendAsync(request, ct);
        Ensure(response, method.Method);
    }

    private static string E(string s) => Uri.EscapeDataString(s);
    private static string Location(string? dir) => dir is null ? "" : "?location%5Bdirectory%5D=" + E(dir);
    public Task<ServerInfo> GetInfoAsync(CancellationToken ct) => Get<ServerInfo>("/api/info", ct);
    public Task<List<OpenCodeProject>> GetProjectsAsync(CancellationToken ct) => Get<List<OpenCodeProject>>("/api/project", ct);
    public async Task<List<OpenCodeModel>> GetModelsAsync(string? directory, CancellationToken ct) => (await Get<ApiData<List<OpenCodeModel>>>("/api/model" + Location(directory), ct)).Data;
    public async Task<List<OpenCodeAgent>> GetAgentsAsync(string? directory, CancellationToken ct) => (await Get<ApiData<List<OpenCodeAgent>>>("/api/agent" + Location(directory), ct)).Data;
    public async Task<List<OpenCodeProvider>> GetProvidersAsync(CancellationToken ct) => (await Get<ApiData<List<OpenCodeProvider>>>("/api/provider", ct)).Data;
    public Task<ApiPage<OpenCodeSession>> GetSessionsAsync(string? cursor, CancellationToken ct) => Get<ApiPage<OpenCodeSession>>("/api/session?limit=100" + (cursor is null ? "" : "&cursor=" + E(cursor)), ct);
    public Task<ApiPage<OpenCodeSession>> GetDirectorySessionsAsync(string directory, string? cursor, CancellationToken ct) => Get<ApiPage<OpenCodeSession>>("/api/session?limit=100&directory=" + E(directory) + (cursor is null ? "" : "&cursor=" + E(cursor)), ct);
    public async Task<OpenCodeSession> GetSessionAsync(string id, CancellationToken ct) => (await Get<ApiData<OpenCodeSession>>("/api/session/" + E(id), ct)).Data;
    public async Task<OpenCodeSession> CreateSessionAsync(CreateSessionRequest request, CancellationToken ct) => (await Write<ApiData<OpenCodeSession>>(HttpMethod.Post, "/api/session", request, ct)).Data;
    public Task UpdateSessionAsync(string id, string? title, bool? archived, List<PermissionRule>? permissions, CancellationToken ct) => Write(HttpMethod.Patch, "/api/session/" + E(id), new { title, archived, permissions }, ct);
    public Task SwitchModelAsync(string id, ModelRef model, CancellationToken ct) => Write(HttpMethod.Post, "/api/session/" + E(id) + "/model", new { model }, ct);
    public Task SwitchAgentAsync(string id, string agent, CancellationToken ct) => Write(HttpMethod.Post, "/api/session/" + E(id) + "/agent", new { agent }, ct);
    public async Task<OpenCodeInbox> SendPromptAsync(string sessionId, PromptRequest prompt, CancellationToken ct) => (await Write<ApiData<OpenCodeInbox>>(HttpMethod.Post, "/api/session/" + E(sessionId) + "/prompt", prompt, ct)).Data;
    public Task CancelInboxAsync(string sessionId, string messageId, CancellationToken ct) => Write(HttpMethod.Delete, "/api/session/" + E(sessionId) + "/inbox/" + E(messageId), null, ct);
    public Task InterruptAsync(string sessionId, CancellationToken ct) => Write(HttpMethod.Post, "/api/session/" + E(sessionId) + "/interrupt", null, ct);
    public Task<ApiPage<OpenCodeMessage>> GetMessagesAsync(string sessionId, string? cursor, CancellationToken ct) => Get<ApiPage<OpenCodeMessage>>("/api/session/" + E(sessionId) + "/message?limit=100" + (cursor is null ? "&order=desc" : "&cursor=" + E(cursor)), ct);
    public async Task<List<OpenCodeInbox>> GetInboxAsync(string sessionId, CancellationToken ct) => (await Get<ApiData<List<OpenCodeInbox>>>("/api/session/" + E(sessionId) + "/inbox", ct)).Data;
    public async Task<List<OpenCodePermission>> GetPermissionsAsync(string sessionId, CancellationToken ct) => (await Get<ApiData<List<OpenCodePermission>>>("/api/session/" + E(sessionId) + "/permission", ct)).Data;
    public Task ReplyPermissionAsync(string sessionId, string requestId, string decision, CancellationToken ct) => Write(HttpMethod.Post, "/api/session/" + E(sessionId) + "/permission/" + E(requestId) + "/reply", new { decision }, ct);
    public async Task<List<OpenCodeDiff>> GetDiffAsync(string sessionId, string? messageId, CancellationToken ct) => (await Get<ApiData<List<OpenCodeDiff>>>("/api/session/" + E(sessionId) + "/diff" + (messageId is null ? "" : "?from=" + E(messageId)), ct)).Data;
    public async IAsyncEnumerable<OpenCodeEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, "/api/event");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        Ensure(response, "SSE");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        await foreach (var ev in SseParser.ReadAsync(reader, ct))
            yield return ev;
    }
}
