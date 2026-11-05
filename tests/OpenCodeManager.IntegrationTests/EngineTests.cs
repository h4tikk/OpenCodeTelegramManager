using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;

namespace OpenCodeManager.IntegrationTests;

public sealed class EngineTests
{
    [Fact]
    public async Task TelegramReplayIsIdempotentAndQueueSurvivesReopen()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var s = await r.SessionAsync("repo");
        var a = await r.Tasks.EnqueueAsync(s.Id, "first", "update:1", 42, default);
        var b = await r.Tasks.EnqueueAsync(s.Id, "first", "update:1", 42, default);
        Assert.Equal(a.Id, b.Id);
        await using var db = await r.Factory.CreateAsync();
        Assert.Single(await db.Tasks.ToListAsync());
    }

    [Fact]
    public async Task CrashDuringAdmissionNeverResendsPrompt()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var s = await r.SessionAsync("repo");
        r.IdleApi(s);
        var t = await r.Tasks.EnqueueAsync(s.Id, "work", "u:1", 42, default);
        await using (var db = await r.Factory.CreateAsync())
        {
            var task = await db.Tasks.SingleAsync();
            task.Status = AgentTaskStatus.Dispatching;
            db.Attempts.Add(new() { TaskId = t.Id, AttemptNumber = 1, OpenCodeMessageId = "msg_uncertain", Status = AttemptStatus.Dispatching });
            await db.SaveChangesAsync();
        }

        await r.Engine.RecoverAsync(default);
        await r.Engine.TickAsync(default);
        await using (var db = await r.Factory.CreateAsync())
            Assert.Equal(AgentTaskStatus.NeedsReconciliation, (await db.Tasks.SingleAsync()).Status);
        r.Api.Verify(x => x.SendPromptAsync(It.IsAny<string>(), It.IsAny<PromptRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatcherEnforcesFifoConcurrencyAndSharedTreeLock()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var a = await r.SessionAsync("a");
        var b = await r.SessionAsync("b");
        var c = await r.SessionAsync("c");
        foreach (var s in new[]
        {
            a,
            b,
            c
        }

        )
            r.IdleApi(s);
        r.Api.Setup(x => x.GetSessionsAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeSession>([], null));
        var admitted = new List<string>();
        r.Api.Setup(x => x.SendPromptAsync(It.IsAny<string>(), It.IsAny<PromptRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((string session, PromptRequest prompt, CancellationToken ct) =>
        {
            admitted.Add(session);
            return new OpenCodeInbox(prompt.Id, session, "user");
        });
        var t1 = await r.Tasks.EnqueueAsync(a.Id, "one", "1", 42, default);
        await Task.Delay(5);
        var t2 = await r.Tasks.EnqueueAsync(b.Id, "two", "2", 42, default);
        await Task.Delay(5);
        var t3 = await r.Tasks.EnqueueAsync(c.Id, "three", "3", 42, default);
        await using (var db = await r.Factory.CreateAsync())
        {
            var t = await db.Tasks.SingleAsync(x => x.Id == t2.Id);
            t.LockKey = (await db.Tasks.SingleAsync(x => x.Id == t1.Id)).LockKey;
            await db.SaveChangesAsync();
        }

        // Use a second logical session inside the same actual repository.
        await using (var db = await r.Factory.CreateAsync())
        {
            var bs = await db.Sessions.SingleAsync(x => x.Id == b.Id);
            bs.WorkingDirectory = a.WorkingDirectory;
            await db.SaveChangesAsync();
        }

        await r.Engine.TickAsync(default);
        Assert.Equal(new[] { a.OpenCodeSessionId, c.OpenCodeSessionId }, admitted);
        await using (var db = await r.Factory.CreateAsync())
        {
            Assert.Equal(AgentTaskStatus.Queued, (await db.Tasks.SingleAsync(x => x.Id == t2.Id)).Status);
            Assert.Equal(2, await db.Attempts.CountAsync());
        }
    }

    [Fact]
    public async Task CompletionRequiresOwnMessageAndFollowingIdleMarker()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var s = await r.SessionAsync("repo");
        r.IdleApi(s);
        var t = await r.Tasks.EnqueueAsync(s.Id, "work", "u", 42, default);
        await using (var db = await r.Factory.CreateAsync())
        {
            var task = await db.Tasks.SingleAsync();
            task.Status = AgentTaskStatus.Running;
            db.Attempts.Add(new() { TaskId = t.Id, AttemptNumber = 1, OpenCodeMessageId = "msg_own", Status = AttemptStatus.Running });
            await db.SaveChangesAsync();
        }

        r.Api.Setup(x => x.GetMessagesAsync(s.OpenCodeSessionId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeMessage>([new("msg_idle", "idle", new(30, null), null, "succeeded", null, null, null), new("msg_answer", "assistant", new(20, 25), null, null, [new("text", "done", null)], null, null), new("msg_own", "user", new(10, null), "work", null, null, null, null)], null));
        await r.Engine.ReconcileAsync(t.Id, default);
        await using (var db = await r.Factory.CreateAsync())
        {
            var task = await db.Tasks.SingleAsync();
            Assert.Equal(AgentTaskStatus.Completed, task.Status);
            Assert.Equal("done", task.ResultPreview);
        }
    }

    [Fact]
    public async Task LostPostResponseBecomesUncertainWithoutRetry()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var s = await r.SessionAsync("repo");
        r.IdleApi(s);
        r.Api.Setup(x => x.GetSessionsAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeSession>([], null));
        r.Api.Setup(x => x.SendPromptAsync(s.OpenCodeSessionId, It.IsAny<PromptRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());
        await r.Tasks.EnqueueAsync(s.Id, "work", "1", 42, default);
        await r.Engine.TickAsync(default);
        await r.Engine.TickAsync(default);
        await using (var db = await r.Factory.CreateAsync())
            Assert.Equal(AgentTaskStatus.NeedsReconciliation, (await db.Tasks.SingleAsync()).Status);
        r.Api.Verify(x => x.SendPromptAsync(s.OpenCodeSessionId, It.IsAny<PromptRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BrowserIsLazyPagedAndBlocksSymlinkEscapes()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        for (int i = 0; i < 20; i++)
            Directory.CreateDirectory(Path.Combine(r.Root, $"dir{i:00}"));
        Directory.CreateDirectory(Path.Combine(r.Root, ".hidden"));
        Directory.CreateSymbolicLink(Path.Combine(r.Root, "escape"), "/etc");
        var first = await r.Browser.GetDirectoriesAsync(r.Root);
        var second = await r.Browser.GetDirectoriesAsync(r.Root, 1);
        Assert.Equal(8, first.Directories.Count);
        Assert.True(first.HasMore);
        Assert.DoesNotContain(first.Directories, x => x.Name == "escape" || x.Name == ".hidden");
        Assert.Equal("dir08", second.Directories[0].Name);
        await using (var db = await r.Factory.CreateAsync())
            Assert.Empty(await db.Projects.ToListAsync());
    }

    [Fact]
    public async Task PermissionsAllowProjectFilesButNeverShellSecretsOrOutside()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var s = await r.SessionAsync("repo");
        Assert.True(r.Permissions.CanAutoApprove("edit", ["Program.cs"], s.WorkingDirectory));
        Assert.False(r.Permissions.CanAutoApprove("read", [".env"], s.WorkingDirectory));
        Assert.False(r.Permissions.CanAutoApprove("shell", ["dotnet test"], s.WorkingDirectory));
        Assert.False(r.Permissions.CanAutoApprove("read", ["/etc/passwd"], s.WorkingDirectory));
    }
}
