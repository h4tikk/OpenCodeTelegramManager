using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCodeManager.Application;
using OpenCodeManager.Worker;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("OpenCode Manager requires Linux");
    return 78;
}

var command = args.FirstOrDefault() ?? "run";
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddManagerLogging();
WorkerConfiguration configuration;
try
{
    configuration = WorkerConfiguration.Load(builder.Configuration);
    configuration.Validate();
}
catch (Exception error)
{
    Console.Error.WriteLine("Invalid manager configuration: " + error.GetType().Name);
    return 78;
}

if (command == "--check-config")
    return configuration.TelegramConfigured ? 0 : 1;
if (command == "--health")
    return HealthCommand.Check(configuration.Storage);
try
{
    using var lease = StorageLease.Acquire(configuration.Storage, command != "--doctor");
    builder.Services.AddManagerWorker(configuration, command == "run");
    using var host = builder.Build();
    return await host.Services.GetRequiredService<WorkerCommands>().ExecuteAsync(host, command, CancellationToken.None);
}
catch (ManagerAlreadyRunningException)
{
    Console.Error.WriteLine("Another manager instance holds the database lock");
    return 73;
}
catch (Exception error)
{
    Console.Error.WriteLine("Manager failed: " + (error is OpenCodeApiException apiError ? apiError.Message : error.GetType().Name));
    return 1;
}
