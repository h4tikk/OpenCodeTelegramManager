namespace OpenCodeManager.Application;
public sealed class OpenCodeApiException(int status, string operation) : Exception($"OpenCode {operation}: HTTP {status}")
{
    public int StatusCode { get; } = status;
    public string Code => StatusCode switch
    {
        401 => "Authentication",
        403 => "Forbidden",
        404 => "NotFound",
        409 => "Conflict",
        429 => "RateLimit",
        >= 500 => "Server",
        _ => "Request"
    };
}
