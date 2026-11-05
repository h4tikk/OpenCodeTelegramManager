using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.UnitTests;
public sealed class SecurityTests
{
    [Theory]
    [InlineData(42, 42, true, true)]
    [InlineData(7, 42, true, false)]
    [InlineData(42, 7, true, false)]
    [InlineData(42, 42, false, false)]
    public void OwnerMustMatchUserPrivateChatAndChatId(long user, long chat, bool isPrivate, bool expected) => Assert.Equal(expected, new OwnerGuard(new() { OwnerUserId = 42 }).IsAuthorized(user, chat, isPrivate));
    [Fact]
    public void MissingOwnerNeverAuthorizes() => Assert.False(new OwnerGuard(new()).IsAuthorized(0, 0, true));
    [Theory]
    [InlineData("session:abcd", true)]
    [InlineData("file:/etc/passwd", false)]
    [InlineData("x:a:b", false)]
    [InlineData("../x:a", false)]
    [InlineData("x:", false)]
    public void CallbackSyntaxIsBounded(string value, bool expected) => Assert.Equal(expected, CallbackData.TryParse(value, out _, out _));
    [Theory]
    [InlineData(AgentTaskStatus.Queued, AgentTaskStatus.Completed, false)]
    [InlineData(AgentTaskStatus.Queued, AgentTaskStatus.Dispatching, true)]
    [InlineData(AgentTaskStatus.Running, AgentTaskStatus.NeedsReconciliation, true)]
    [InlineData(AgentTaskStatus.Completed, AgentTaskStatus.Running, false)]
    public void StateTransitionsAreConstrained(AgentTaskStatus from, AgentTaskStatus to, bool expected) => Assert.Equal(expected, TaskStates.CanTransition(from, to));
    [Fact]
    public void CanonicalPathsBlockPrefixSiblingAndSymlinkEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), "manager-path-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var outside = root + "-outside";
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "escape"), outside);
            var p = new PathPolicy(new() { AllowedRoots = [root] });
            Assert.Throws<UnauthorizedAccessException>(() => p.DemandAllowed(outside));
            Assert.Throws<UnauthorizedAccessException>(() => p.DemandAllowed(Path.Combine(root, "..", Path.GetFileName(outside))));
            Assert.Throws<UnauthorizedAccessException>(() => p.DemandAllowed(Path.Combine(root, "escape")));
            Assert.Equal(root, p.DemandAllowed(root));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outside);
        }
    }
}
