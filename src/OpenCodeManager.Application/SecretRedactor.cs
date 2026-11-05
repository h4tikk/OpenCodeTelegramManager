using System.Text.RegularExpressions;
namespace OpenCodeManager.Application;

public sealed partial class SecretRedactor(TelegramOptions telegram, OpenCodeOptions openCode)
{
    public string Redact(string? text)
    {
        var value = text ?? "";
        foreach (var secret in new[] { telegram.Token, openCode.Password }.Where(x => x.Length > 3)) value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        value = CredentialUrl().Replace(value, "$1[REDACTED]@");
        value = BotToken().Replace(value, "[REDACTED]");
        value = CredentialLine().Replace(value, "$1[REDACTED]");
        value = PrivateKey().Replace(value, "[PRIVATE KEY REDACTED]");
        return value;
    }
    [GeneratedRegex(@"(https?://)[^\s/@]+:[^\s/@]+@", RegexOptions.IgnoreCase)] private static partial Regex CredentialUrl();
    [GeneratedRegex(@"\b\d{6,12}:[A-Za-z0-9_-]{25,}\b")] private static partial Regex BotToken();
    [GeneratedRegex(@"(?im)^([^\r\n]*(?:password|passwd|api[_-]?key|secret|token|authorization)\s*[=:]\s*)[^\r\n]+$")] private static partial Regex CredentialLine();
    [GeneratedRegex(@"-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?-----END [^-]*PRIVATE KEY-----")] private static partial Regex PrivateKey();
}
