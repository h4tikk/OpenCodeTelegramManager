using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed partial class FileAccessPolicy(IPathPolicy paths, OwnerGuard guard, FileStorageOptions options)
{
    public static bool IsSecretName(string path) => path.Split(Path.DirectorySeparatorChar).Any(x => x.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || x is ".ssh" or ".gnupg" or ".aws" or ".git" or "secrets" || x.Contains("credential", StringComparison.OrdinalIgnoreCase) || x.Contains("secret", StringComparison.OrdinalIgnoreCase) || x is "auth.json" or "service.json" or ".npmrc" or ".netrc" || x.StartsWith("id_rsa", StringComparison.Ordinal) || x.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".pem", StringComparison.OrdinalIgnoreCase));
    public string ReadPath(string path, long owner)
    {
        guard.Demand(owner);
        var canonical = paths.DemandAllowed(path);
        if (IsSecretName(canonical) || IsSecretName(Path.GetFullPath(path)))
            throw new UnauthorizedAccessException("Secret file policy denied transfer");
        if (!File.Exists(canonical))
            throw new FileNotFoundException();
        return canonical;
    }

    public async Task CheckContentAsync(Stream stream, CancellationToken ct)
    {
        // Inspect the entire byte stream in bounded windows before any transfer. This detects common plaintext credentials, not arbitrary encrypted secrets.
        var buffer = new byte[64 * 1024];
        string tail = "";
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var text = tail + Encoding.UTF8.GetString(buffer, 0, count);
            if (SecretContent().IsMatch(text))
                throw new UnauthorizedAccessException("Credential-like content blocked by transfer policy");
            tail = text[^Math.Min(512, text.Length)..];
        }

        stream.Position = 0;
    }

    [GeneratedRegex("""(?i)(-----BEGIN [^-]*PRIVATE KEY-----|[0-9]{6,12}:[A-Za-z0-9_-]{25,}|sk-[A-Za-z0-9_-]{24,}|(?:password|passwd|api[_-]?key|secret|access[_-]?token)"?\s*[=:]\s*"?[^\s",;}]{5,})""")]
    private static partial Regex SecretContent();
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || name != Path.GetFileName(name) || name is "." or ".." || name.Contains('\\') || name.Any(char.IsControl) || IsSecretName(name))
            throw new ArgumentException("Unsafe file name");
        return name;
    }

    public string StoredPath(string path)
    {
        var canonical = paths.Canonicalize(path);
        var root = paths.Canonicalize(options.DataDirectory);
        if (!canonical.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || IsSecretName(Path.GetFileName(canonical)))
            throw new UnauthorizedAccessException("Attachment escaped storage root");
        return canonical;
    }
}
