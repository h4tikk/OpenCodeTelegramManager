using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCodeManager.Application;
namespace OpenCodeManager.Infrastructure;
public static class Registration
{
    public static IServiceCollection AddManagerInfrastructure(this IServiceCollection services, StorageOptions storage, OpenCodeOptions openCode)
    {
        services.AddDbContextFactory<ManagerDbContext>(o => o.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, DefaultTimeout = 15, ForeignKeys = true }.ToString()));
        services.AddSingleton<IManagerDbFactory, ManagerDbFactory>();
        services.AddSingleton<IPathPolicy, PathPolicy>();
        services.AddSingleton<ManagerService>(); services.AddSingleton<PermissionManager>(); services.AddSingleton<TaskEngine>(); services.AddSingleton<DirectoryNavigation>();
        services.AddSingleton<IFileSystemBrowser, FileSystemBrowserService>(); services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>(); services.AddSingleton<SecretRedactor>();
        services.AddHttpClient<IOpenCodeClient, OpenCodeApiClient>(http => http.Timeout = TimeSpan.FromSeconds(openCode.TimeoutSeconds)).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        return services;
    }
}
