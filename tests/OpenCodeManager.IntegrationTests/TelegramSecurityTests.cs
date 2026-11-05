using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCodeManager.Application;
using OpenCodeManager.Infrastructure;
using OpenCodeManager.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OpenCodeManager.IntegrationTests;
public sealed class TelegramSecurityTests
{
    [Theory]
    [InlineData(7, 42, ChatType.Private, false)]
    [InlineData(42, 42, ChatType.Group, false)]
    [InlineData(42, 7, ChatType.Private, false)]
    [InlineData(7, 42, ChatType.Private, true)]
    [InlineData(42, 42, ChatType.Supergroup, true)]
    public async Task UnauthorizedMessagesFilesAndCallbacksNeverReachApi(long userId, long chatId, ChatType chatType, bool callback)
    {
        await using var rig = new TestRig();
        await rig.InitializeAsync();
        var bot = new Mock<ITelegramBotClient>(MockBehavior.Strict);
        var security = new BotSecurityOptions
        {
            OwnerUserId = 42
        };
        var options = new TelegramOptions();
        var services = new ServiceCollection();
        services.AddLogging();
        var storage = new StorageOptions
        {
            DatabasePath = Path.Combine(rig.Root, "state.db")
        };
        var openCode = new OpenCodeOptions();
        services.AddSingleton(storage);
        services.AddSingleton(openCode);
        services.AddSingleton(options);
        services.AddSingleton(security);
        services.AddSingleton(new TaskExecutionOptions());
        services.AddSingleton(new ProjectOptions { AllowedRoots = [rig.Root] });
        services.AddSingleton(new FileStorageOptions { DataDirectory = Path.Combine(rig.Root, "files") });
        services.AddSingleton<OwnerGuard>();
        services.AddManagerInfrastructure(storage, openCode);
        services.AddSingleton<IManagerDbFactory>(rig.Factory);
        services.AddSingleton<IOpenCodeClient>(rig.Api.Object);
        services.AddSingleton<ITelegramBotClient>(bot.Object);
        services.AddManagerTelegram();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var ui = provider.GetRequiredService<TelegramUpdateHandler>();
        var message = new Message
        {
            Id = 1,
            Chat = new()
            {
                Id = chatId,
                Type = chatType
            },
            From = new()
            {
                Id = userId,
                FirstName = "Test"
            },
            Text = "/projects",
            Document = new()
            {
                FileId = "ignored",
                FileUniqueId = "ignored",
                FileName = "foo.cs"
            }
        };
        var update = callback ? new Update
        {
            Id = 1,
            CallbackQuery = new()
            {
                Id = "c",
                From = message.From,
                Message = message,
                ChatInstance = "x",
                Data = "sync:0"
            }
        }

        : new Update
        {
            Id = 1,
            Message = message
        };
        await ui.HandleAsync(update, default);
        bot.VerifyNoOtherCalls();
        rig.Api.VerifyNoOtherCalls();
    }
}
