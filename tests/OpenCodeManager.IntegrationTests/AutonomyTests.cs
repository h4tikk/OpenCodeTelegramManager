using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.IntegrationTests;
public sealed class AutonomyTests
{
    [Fact]
    public async Task MissingSandboxPluginRefusesActivation()
    {
        await using var rig = new TestRig();
        await rig.InitializeAsync();
        var session = await rig.SessionAsync("project");
        rig.Api.Setup(x => x.GetPluginsAsync(session.WorkingDirectory, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var guard = new SessionAutonomy(rig.Api.Object, rig.Factory, rig.Paths, new());
        await Assert.ThrowsAsync<AutonomyUnavailableException>(() => guard.ConfigureAsync(TestRig.Remote(session), session.WorkingDirectory, default));
        rig.Api.Verify(x => x.SetSessionMetadataAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivationPreservesUserMetadataAndPinsDirectory()
    {
        await using var rig = new TestRig();
        await rig.InitializeAsync();
        var session = await rig.SessionAsync("project");
        rig.Api.Setup(x => x.GetPluginsAsync(session.WorkingDirectory, It.IsAny<CancellationToken>())).ReturnsAsync([new(SessionAutonomy.PluginId, new("active"))]);
        Dictionary<string, JsonElement>? saved = null;
        rig.Api.Setup(x => x.SetSessionMetadataAsync(session.OpenCodeSessionId, It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>())).Callback((string _, Dictionary<string, JsonElement> metadata, CancellationToken _) => saved = metadata).Returns(Task.CompletedTask);
        var remote = TestRig.Remote(session)with
        {
            Metadata = new()
            {
                ["existing"] = JsonSerializer.SerializeToElement("preserve")
            }
        };
        await new SessionAutonomy(rig.Api.Object, rig.Factory, rig.Paths, new()).ConfigureAsync(remote, session.WorkingDirectory, default);
        Assert.Equal("preserve", saved!["existing"].GetString());
        Assert.Equal(session.WorkingDirectory, saved["opencodeManagerAutonomy"].GetProperty("directory").GetString());
    }

    [Fact]
    public async Task AutonomousFallbackRejectsUnisolatedShellWithoutAskingOwner()
    {
        await using var rig = new TestRig();
        await rig.InitializeAsync();
        var session = await rig.SessionAsync("project");
        var task = await rig.Tasks.EnqueueAsync(session.Id, "work", "request", 42, default);
        rig.Api.Setup(x => x.GetPermissionsAsync(session.OpenCodeSessionId, It.IsAny<CancellationToken>())).ReturnsAsync([new("perm_fixture", session.OpenCodeSessionId, "shell", ["dotnet test"], null)]);
        rig.Api.Setup(x => x.ReplyPermissionAsync(session.OpenCodeSessionId, "perm_fixture", "reject", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var manager = new PermissionManager(rig.Factory, rig.Api.Object, rig.Paths, new(new() { OwnerUserId = 42 }), NullLogger<PermissionManager>.Instance, new() { Enabled = true });
        await manager.SyncAsync(task, session, default);
        await using var db = await rig.Factory.CreateAsync();
        Assert.Equal(ApprovalStatus.Rejected, (await db.Permissions.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("node", "test_policy.mjs")]
    [InlineData("python3", "test_sandbox.py")]
    public async Task NativeAutonomyTests(string executable, string script)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenCodeTelegramManager.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(root.FullName, "deploy/autonomy", script));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
