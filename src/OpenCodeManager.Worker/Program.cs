using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCodeManager.Application;
using OpenCodeManager.Infrastructure;
using OpenCodeManager.Telegram;
using OpenCodeManager.Worker;
using Telegram.Bot;

if (!OperatingSystem.IsLinux()) { Console.Error.WriteLine("OpenCode Manager requires Linux"); return 78; }
var command = args.FirstOrDefault() ?? "run";
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddSimpleConsole(o => { o.SingleLine=true; o.IncludeScopes=true; o.TimestampFormat="yyyy-MM-dd HH:mm:ss "; o.UseUtcTimestamp=true; });
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command",LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient",LogLevel.None);
T Load<T>(string section) where T : class,new() => builder.Configuration.GetSection(section).Get<T>() ?? new();
var environmentFile = builder.Configuration["Manager:EnvironmentFile"];
if (!string.IsNullOrEmpty(environmentFile))
{
    try
    {
        var values = new Dictionary<string,string?>();
        foreach (var line in File.ReadLines(environmentFile))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var parts=line.Split('=',2); if(parts.Length!=2)throw new InvalidDataException("Invalid environment file line");
            if (!string.IsNullOrWhiteSpace(parts[1])) values[parts[0].Trim().Replace("__", ":")] = parts[1].Trim();
        }
        builder.Configuration.AddInMemoryCollection(values).AddEnvironmentVariables();
    }
    catch(Exception e){Console.Error.WriteLine("Cannot load protected environment file: "+e.GetType().Name);return 78;}
}
var telegram=Load<TelegramOptions>("Telegram");var security=Load<BotSecurityOptions>("BotSecurity");var openCode=Load<OpenCodeOptions>("OpenCode");var execution=Load<TaskExecutionOptions>("TaskExecution");var storage=Load<StorageOptions>("Storage");var projects=Load<ProjectOptions>("Projects");var fileOptions=Load<FileStorageOptions>("Files");
bool configured=security.OwnerUserId>0 && !string.IsNullOrWhiteSpace(telegram.Token);
if(command=="--check-config")return configured?0:1;
if(command=="run" && !configured) Console.Error.WriteLine("Waiting for Telegram__Token and BotSecurity__OwnerUserId; Telegram and dispatcher are disabled. Configure credentials and recreate container.");
if(!Uri.TryCreate(openCode.BaseUrl,UriKind.Absolute,out var endpoint)||(!endpoint.IsLoopback && endpoint.Scheme!="https")||endpoint.UserInfo.Length>0||endpoint.Query.Length>0){Console.Error.WriteLine("OpenCode__BaseUrl must be loopback HTTP or HTTPS, without embedded credentials/query");return 78;}
if(execution.MaxConcurrentTasks is <1 or >16 || execution.PollSeconds is <1 or >60 || execution.MaxDurationMinutes is <1 or >1440 || execution.MaxToolCalls <1){Console.Error.WriteLine("Invalid TaskExecution limits");return 78;}
storage.DatabasePath=Path.GetFullPath(storage.DatabasePath);var dataDirectory=Path.GetDirectoryName(storage.DatabasePath)!;
if(command=="--health") return File.Exists(Path.Combine(dataDirectory,"worker.heartbeat")) && DateTime.UtcNow-File.GetLastWriteTimeUtc(Path.Combine(dataDirectory,"worker.heartbeat"))<TimeSpan.FromSeconds(45)?0:1;
Directory.CreateDirectory(dataDirectory); File.SetUnixFileMode(dataDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
FileStream? instanceLock=null;
try { if(command!="--doctor") instanceLock = new FileStream(Path.Combine(dataDirectory,"manager.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
catch(IOException){Console.Error.WriteLine("Another manager instance holds the database lock");return 73;}
using var heldLock=instanceLock;
foreach(var instance in new object[]{telegram,security,openCode,execution,storage,projects,fileOptions})builder.Services.AddSingleton(instance.GetType(),instance);
builder.Services.AddSingleton<OwnerGuard>();builder.Services.AddManagerInfrastructure(storage,openCode);
if(command=="run")builder.Services.AddHostedService<HealthWorker>();
if(command=="run" && configured)
{
    builder.Services.AddSingleton<ITelegramBotClient>(_=>new TelegramBotClient(telegram.Token));
    builder.Services.AddHostedService<DispatcherWorker>();builder.Services.AddHostedService<OpenCodeEventListener>();builder.Services.AddHostedService<TelegramInterface>();
}
using var host=builder.Build();
try
{
    if(command!="--doctor") { await using var db=await host.Services.GetRequiredService<IDbContextFactory<ManagerDbContext>>().CreateDbContextAsync(); await DatabaseInitializer.InitializeAsync(db); }
    File.SetUnixFileMode(storage.DatabasePath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    if(command!="--doctor")await host.Services.GetRequiredService<DirectoryNavigation>().InitializeRootsAsync(CancellationToken.None);
    if(command=="--migrate"){Console.WriteLine("SQLite migrations applied; database="+storage.DatabasePath);return 0;}
    if(command is "--doctor" or "--sync")
    {
        var api=host.Services.GetRequiredService<IOpenCodeClient>();
        var info=await api.GetInfoAsync(CancellationToken.None);
        if(!info.Version.StartsWith("2."))throw new InvalidOperationException("Unsupported OpenCode major version");
        var models=await api.GetModelsAsync(null,CancellationToken.None);var agents=await api.GetAgentsAsync(null,CancellationToken.None);var remoteProjects=await api.GetProjectsAsync(CancellationToken.None);
        int sessions=0;string? cursor=null;do{var page=await api.GetSessionsAsync(cursor,CancellationToken.None);sessions+=page.Data.Count;cursor=page.Cursor?.Next;}while(cursor is not null);
        if(command=="--sync")await host.Services.GetRequiredService<ManagerService>().SyncAsync(true,CancellationToken.None);
        await using var db=await host.Services.GetRequiredService<IDbContextFactory<ManagerDbContext>>().CreateDbContextAsync();
        Console.WriteLine(JsonSerializer.Serialize(new{openCode=info.Version,models=models.Count,agents=agents.Count,projects=remoteProjects.Count,sessions,sqlite="OK",database=storage.DatabasePath,migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray(),telegramConfigured=configured,localProjects=await db.Projects.CountAsync(),mappedSessions=await db.Sessions.CountAsync()},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
    if(command!="run"){Console.Error.WriteLine("Commands: run, --doctor, --sync, --migrate, --check-config");return 64;}
    try{await host.Services.GetRequiredService<ManagerService>().SyncAsync(true,CancellationToken.None);}catch(Exception e){host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogWarning("Initial OpenCode synchronization unavailable: {ErrorType}",e.GetType().Name);}
    await host.Services.GetRequiredService<IFileManager>().CleanupAsync(CancellationToken.None);
    await host.RunAsync();return 0;
}
catch(Exception e){Console.Error.WriteLine("Manager failed: "+(e is OpenCodeApiException apiError?apiError.Message:e.GetType().Name));return 1;}
