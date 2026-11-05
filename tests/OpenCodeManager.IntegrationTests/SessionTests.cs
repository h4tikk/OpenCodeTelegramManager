using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;

namespace OpenCodeManager.IntegrationTests;

public sealed class SessionTests
{
    [Fact]
    public async Task RepeatedSyncReusesMainSessionAndKeepsRequestedDirectory()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var dir = Path.Combine(r.Root, "Category");
        Directory.CreateDirectory(dir);
        var remote = new List<OpenCodeSession>();
        r.Api.Setup(x => x.GetInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ServerInfo("2.2.5", 123, []));
        r.Api.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        r.Api.Setup(x => x.GetSessionsAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(() => new ApiPage<OpenCodeSession>(remote, null));
        r.Api.Setup(x => x.GetSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OpenCodeApiException(404, "GET"));
        r.Api.Setup(x => x.CreateSessionAsync(It.IsAny<CreateSessionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((CreateSessionRequest input, CancellationToken ct) =>
        {
            Assert.Equal(dir, input.Location.Directory);
            var result = new OpenCodeSession(input.Id, "proj", input.Title, input.Location, new(1, 1, null, null, null), null, null, null, input.Permissions);
            remote.Add(result);
            return result;
        });
        await r.Sessions.SyncAsync(true, default);
        await r.Sessions.SyncAsync(true, default);
        r.Api.Verify(x => x.CreateSessionAsync(It.IsAny<CreateSessionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var db = await r.Factory.CreateAsync();
        Assert.Single(await db.Sessions.ToListAsync());
        Assert.True((await db.Sessions.SingleAsync()).IsMain);
    }

    [Fact]
    public async Task OpenCodeOutageLeavesQueuedTaskAndRecoversWithoutDuplicateSubmission()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var s = await r.SessionAsync("repo");
        r.IdleApi(s);
        r.Api.SetupSequence(x => x.GetSessionAsync(s.OpenCodeSessionId, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException()).ReturnsAsync(TestRig.Remote(s));
        r.Api.Setup(x => x.GetSessionsAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeSession>([], null));
        r.Api.Setup(x => x.SendPromptAsync(s.OpenCodeSessionId, It.IsAny<PromptRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((string id, PromptRequest prompt, CancellationToken ct) => new OpenCodeInbox(prompt.Id, id, "user"));
        await r.Tasks.EnqueueAsync(s.Id, "work", "offline:1", 42, default);
        await Assert.ThrowsAsync<HttpRequestException>(() => r.Engine.TickAsync(default));
        await using (var db = await r.Factory.CreateAsync())
            Assert.Equal(AgentTaskStatus.Queued, (await db.Tasks.SingleAsync()).Status);
        await r.Engine.TickAsync(default);
        r.Api.Verify(x => x.SendPromptAsync(s.OpenCodeSessionId, It.IsAny<PromptRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
