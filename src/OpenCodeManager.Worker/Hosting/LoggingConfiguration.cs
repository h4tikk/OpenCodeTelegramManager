using Microsoft.Extensions.Logging;

namespace OpenCodeManager.Worker;
public static class LoggingConfiguration
{
    public static void AddManagerLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            options.UseUtcTimestamp = true;
        });
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
    }
}
