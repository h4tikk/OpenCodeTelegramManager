using Microsoft.Extensions.Configuration;

namespace OpenCodeManager.Worker;
public static class EnvironmentFileConfiguration
{
    public static void AddProtectedEnvironmentFile(this ConfigurationManager configuration)
    {
        var path = configuration["Manager:EnvironmentFile"];
        if (string.IsNullOrEmpty(path))
            return;
        var values = new Dictionary<string, string?>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                throw new InvalidDataException("Invalid environment file line");
            if (!string.IsNullOrWhiteSpace(parts[1]))
                values[parts[0].Trim().Replace("__", ":")] = parts[1].Trim();
        }

        configuration.AddInMemoryCollection(values).AddEnvironmentVariables();
    }
}
