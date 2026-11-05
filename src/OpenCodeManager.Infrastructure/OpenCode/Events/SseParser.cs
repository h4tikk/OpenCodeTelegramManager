using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public static class SseParser
{
    public static async IAsyncEnumerable<OpenCodeEvent> ReadAsync(TextReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct)is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length == 0)
                    continue;
                OpenCodeEvent? ev = null;
                try
                {
                    ev = JsonSerializer.Deserialize<OpenCodeEvent>(data.ToString(), OpenCodeApiClient.JsonOptions);
                }
                catch (JsonException)
                {
                }

                data.Clear();
                if (ev is not null && !string.IsNullOrEmpty(ev.Type))
                    yield return ev;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 1024 * 1024)
                    throw new IOException("Oversized SSE frame");
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line[5..].TrimStart());
            }
        }
    }
}
