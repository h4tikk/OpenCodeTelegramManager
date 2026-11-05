namespace OpenCodeManager.Application;
public sealed class OpenCodeOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:49374";
    public string Username { get; set; } = "opencode";
    public string Password { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}
