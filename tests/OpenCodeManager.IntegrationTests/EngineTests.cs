using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Moq;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using OpenCodeManager.Infrastructure;
namespace OpenCodeManager.IntegrationTests;

public sealed class TestFactory(string path) : IManagerDbFactory
{
    public Task<IManagerDb> CreateAsync(CancellationToken ct = default) => Task.FromResult<IManagerDb>(new ManagerDbContext(ManagerDbContext.Options(path)));
}
public sealed class TestRig : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(),"manager-test-"+Guid.NewGuid());
    public TestFactory Factory { get; }
    public Mock<IOpenCodeClient> Api { get; } = new(MockBehavior.Strict);
    public ManagerService Manager { get; }
    public PermissionManager Permissions { get; }
    public TaskEngine Engine { get; }
    public PathPolicy Paths { get; }
    public DirectoryNavigation Navigation { get; }
    public FileSystemBrowserService Browser { get; }
    public TestRig()
    {
        Directory.CreateDirectory(Root); Factory=new(Path.Combine(Root,"state.db"));
        var options=new ProjectOptions { AllowedRoots=[Root] };Paths=new(options);var guard=new OwnerGuard(new(){OwnerUserId=42});
        Manager=new(Factory,Api.Object,Paths,guard,NullLogger<ManagerService>.Instance);
        Permissions=new(Factory,Api.Object,Paths,guard,NullLogger<PermissionManager>.Instance);
        Engine=new(Factory,Api.Object,Paths,Permissions,new(){MaxConcurrentTasks=2},NullLogger<TaskEngine>.Instance);
        Navigation=new(Factory,Paths,options,guard);Browser=new(Paths,Manager,Navigation,Factory,guard);
    }
    public async Task InitializeAsync()
    {
        await using var db=new ManagerDbContext(ManagerDbContext.Options(Path.Combine(Root,"state.db")));await DatabaseInitializer.InitializeAsync(db);
    }
    public async Task<AgentSession> SessionAsync(string name)
    {
        var dir=Path.Combine(Root,name);Directory.CreateDirectory(dir);
        await using var db=await Factory.CreateAsync();var p=new Project{Name=name,AbsolutePath=dir};db.Projects.Add(p);
        var s=new AgentSession{ProjectId=p.Id,WorkingDirectory=dir,OpenCodeSessionId="ses_"+name,Title=name};db.Sessions.Add(s);await db.SaveChangesAsync();return s;
    }
    public static OpenCodeSession Remote(AgentSession s)=>new(s.OpenCodeSessionId,"proj",s.Title,new(s.WorkingDirectory),new(1,1,null,null,null),null,null,null,null);
    public void IdleApi(AgentSession s)
    {
        Api.Setup(x=>x.GetSessionAsync(s.OpenCodeSessionId,It.IsAny<CancellationToken>())).ReturnsAsync(Remote(s));
        Api.Setup(x=>x.GetInboxAsync(s.OpenCodeSessionId,It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Api.Setup(x=>x.GetPermissionsAsync(s.OpenCodeSessionId,It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Api.Setup(x=>x.GetMessagesAsync(s.OpenCodeSessionId,It.IsAny<string?>(),It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeMessage>([],null));
        Api.Setup(x=>x.UpdateSessionAsync(s.OpenCodeSessionId,null,null,It.IsAny<List<PermissionRule>>(),It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }
    public ValueTask DisposeAsync(){SqliteConnection.ClearAllPools();Directory.Delete(Root,true);return ValueTask.CompletedTask;}
}
public sealed class EngineTests
{
    [Fact]
    public async Task TelegramReplayIsIdempotentAndQueueSurvivesReopen()
    {
        await using var r=new TestRig();await r.InitializeAsync();var s=await r.SessionAsync("repo");
        var a=await r.Manager.EnqueueAsync(s.Id,"first","update:1",42,default);var b=await r.Manager.EnqueueAsync(s.Id,"first","update:1",42,default);
        Assert.Equal(a.Id,b.Id);await using var db=await r.Factory.CreateAsync();Assert.Single(await db.Tasks.ToListAsync());
    }
    [Fact]
    public async Task CrashDuringAdmissionNeverResendsPrompt()
    {
        await using var r=new TestRig();await r.InitializeAsync();var s=await r.SessionAsync("repo");r.IdleApi(s);
        var t=await r.Manager.EnqueueAsync(s.Id,"work","u:1",42,default);
        await using(var db=await r.Factory.CreateAsync())
        {
            var task=await db.Tasks.SingleAsync();task.Status=AgentTaskStatus.Dispatching;db.Attempts.Add(new(){TaskId=t.Id,AttemptNumber=1,OpenCodeMessageId="msg_uncertain",Status=AttemptStatus.Dispatching});await db.SaveChangesAsync();
        }
        await r.Engine.RecoverAsync(default);await r.Engine.TickAsync(default);
        await using(var db=await r.Factory.CreateAsync())Assert.Equal(AgentTaskStatus.NeedsReconciliation,(await db.Tasks.SingleAsync()).Status);
        r.Api.Verify(x=>x.SendPromptAsync(It.IsAny<string>(),It.IsAny<PromptRequest>(),It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task DispatcherEnforcesFifoConcurrencyAndSharedTreeLock()
    {
        await using var r=new TestRig();await r.InitializeAsync();var a=await r.SessionAsync("a");var b=await r.SessionAsync("b");var c=await r.SessionAsync("c");
        foreach(var s in new[]{a,b,c})r.IdleApi(s);
        r.Api.Setup(x=>x.GetSessionsAsync(null,It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeSession>([],null));
        var admitted=new List<string>();r.Api.Setup(x=>x.SendPromptAsync(It.IsAny<string>(),It.IsAny<PromptRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync((string session,PromptRequest prompt,CancellationToken ct)=>{admitted.Add(session);return new OpenCodeInbox(prompt.Id,session,"user");});
        var t1=await r.Manager.EnqueueAsync(a.Id,"one","1",42,default);await Task.Delay(5);var t2=await r.Manager.EnqueueAsync(b.Id,"two","2",42,default);await Task.Delay(5);var t3=await r.Manager.EnqueueAsync(c.Id,"three","3",42,default);
        await using(var db=await r.Factory.CreateAsync()){var t=await db.Tasks.SingleAsync(x=>x.Id==t2.Id);t.LockKey=(await db.Tasks.SingleAsync(x=>x.Id==t1.Id)).LockKey;await db.SaveChangesAsync();}
        // Use a second logical session inside the same actual repository.
        await using(var db=await r.Factory.CreateAsync()){var bs=await db.Sessions.SingleAsync(x=>x.Id==b.Id);bs.WorkingDirectory=a.WorkingDirectory;await db.SaveChangesAsync();}
        await r.Engine.TickAsync(default);
        Assert.Equal(new[]{a.OpenCodeSessionId,c.OpenCodeSessionId},admitted);
        await using(var db=await r.Factory.CreateAsync()){Assert.Equal(AgentTaskStatus.Queued,(await db.Tasks.SingleAsync(x=>x.Id==t2.Id)).Status);Assert.Equal(2,await db.Attempts.CountAsync());}
    }
    [Fact]
    public async Task CompletionRequiresOwnMessageAndFollowingIdleMarker()
    {
        await using var r=new TestRig();await r.InitializeAsync();var s=await r.SessionAsync("repo");r.IdleApi(s);var t=await r.Manager.EnqueueAsync(s.Id,"work","u",42,default);
        await using(var db=await r.Factory.CreateAsync()){var task=await db.Tasks.SingleAsync();task.Status=AgentTaskStatus.Running;db.Attempts.Add(new(){TaskId=t.Id,AttemptNumber=1,OpenCodeMessageId="msg_own",Status=AttemptStatus.Running});await db.SaveChangesAsync();}
        r.Api.Setup(x=>x.GetMessagesAsync(s.OpenCodeSessionId,null,It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeMessage>([
            new("msg_idle","idle",new(30,null),null,"succeeded",null,null,null),new("msg_answer","assistant",new(20,25),null,null,[new("text","done",null)],null,null),new("msg_own","user",new(10,null),"work",null,null,null,null)],null));
        await r.Engine.ReconcileAsync(t.Id,default);
        await using(var db=await r.Factory.CreateAsync()){var task=await db.Tasks.SingleAsync();Assert.Equal(AgentTaskStatus.Completed,task.Status);Assert.Equal("done",task.ResultPreview);}
    }
    [Fact]
    public async Task LostPostResponseBecomesUncertainWithoutRetry()
    {
        await using var r=new TestRig();await r.InitializeAsync();var s=await r.SessionAsync("repo");r.IdleApi(s);
        r.Api.Setup(x=>x.GetSessionsAsync(null,It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeSession>([],null));
        r.Api.Setup(x=>x.SendPromptAsync(s.OpenCodeSessionId,It.IsAny<PromptRequest>(),It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());
        await r.Manager.EnqueueAsync(s.Id,"work","1",42,default);await r.Engine.TickAsync(default);await r.Engine.TickAsync(default);
        await using(var db=await r.Factory.CreateAsync())Assert.Equal(AgentTaskStatus.NeedsReconciliation,(await db.Tasks.SingleAsync()).Status);
        r.Api.Verify(x=>x.SendPromptAsync(s.OpenCodeSessionId,It.IsAny<PromptRequest>(),It.IsAny<CancellationToken>()),Times.Once);
    }
    [Fact]
    public async Task BrowserIsLazyPagedAndBlocksSymlinkEscapes()
    {
        await using var r=new TestRig();await r.InitializeAsync();for(int i=0;i<20;i++)Directory.CreateDirectory(Path.Combine(r.Root,$"dir{i:00}"));Directory.CreateDirectory(Path.Combine(r.Root,".hidden"));Directory.CreateSymbolicLink(Path.Combine(r.Root,"escape"),"/etc");
        var first=await r.Browser.GetDirectoriesAsync(r.Root);var second=await r.Browser.GetDirectoriesAsync(r.Root,1);
        Assert.Equal(8,first.Directories.Count);Assert.True(first.HasMore);Assert.DoesNotContain(first.Directories,x=>x.Name=="escape"||x.Name==".hidden");Assert.Equal("dir08",second.Directories[0].Name);
        await using(var db=await r.Factory.CreateAsync())Assert.Empty(await db.Projects.ToListAsync());
    }
    [Fact]
    public async Task PermissionsAllowProjectFilesButNeverShellSecretsOrOutside()
    {
        await using var r=new TestRig();await r.InitializeAsync();var s=await r.SessionAsync("repo");
        Assert.True(r.Permissions.CanAutoApprove("edit",["Program.cs"],s.WorkingDirectory));
        Assert.False(r.Permissions.CanAutoApprove("read",[".env"],s.WorkingDirectory));Assert.False(r.Permissions.CanAutoApprove("shell",["dotnet test"],s.WorkingDirectory));Assert.False(r.Permissions.CanAutoApprove("read",["/etc/passwd"],s.WorkingDirectory));
    }
}

public sealed class SessionTests
{
    [Fact]
    public async Task RepeatedSyncReusesMainSessionAndKeepsRequestedDirectory()
    {
        await using var r=new TestRig();await r.InitializeAsync();var dir=Path.Combine(r.Root,"Category");Directory.CreateDirectory(dir);var remote=new List<OpenCodeSession>();
        r.Api.Setup(x=>x.GetInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ServerInfo("2.2.5",123,[]));
        r.Api.Setup(x=>x.GetProjectsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        r.Api.Setup(x=>x.GetSessionsAsync(null,It.IsAny<CancellationToken>())).ReturnsAsync(()=>new ApiPage<OpenCodeSession>(remote,null));
        r.Api.Setup(x=>x.GetSessionAsync(It.IsAny<string>(),It.IsAny<CancellationToken>())).ThrowsAsync(new OpenCodeApiException(404,"GET"));
        r.Api.Setup(x=>x.CreateSessionAsync(It.IsAny<CreateSessionRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync((CreateSessionRequest input,CancellationToken ct)=>
        {
            Assert.Equal(dir,input.Location.Directory);var result=new OpenCodeSession(input.Id,"proj",input.Title,input.Location,new(1,1,null,null,null),null,null,null,input.Permissions);remote.Add(result);return result;
        });
        await r.Manager.SyncAsync(true,default);await r.Manager.SyncAsync(true,default);
        r.Api.Verify(x=>x.CreateSessionAsync(It.IsAny<CreateSessionRequest>(),It.IsAny<CancellationToken>()),Times.Once);
        await using var db=await r.Factory.CreateAsync();Assert.Single(await db.Sessions.ToListAsync());Assert.True((await db.Sessions.SingleAsync()).IsMain);
    }
    [Fact]
    public async Task OpenCodeOutageLeavesQueuedTaskAndRecoversWithoutDuplicateSubmission()
    {
        await using var r=new TestRig();await r.InitializeAsync();var s=await r.SessionAsync("repo");r.IdleApi(s);
        r.Api.SetupSequence(x=>x.GetSessionAsync(s.OpenCodeSessionId,It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException()).ReturnsAsync(TestRig.Remote(s));
        r.Api.Setup(x=>x.GetSessionsAsync(null,It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeSession>([],null));
        r.Api.Setup(x=>x.SendPromptAsync(s.OpenCodeSessionId,It.IsAny<PromptRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync((string id,PromptRequest prompt,CancellationToken ct)=>new OpenCodeInbox(prompt.Id,id,"user"));
        await r.Manager.EnqueueAsync(s.Id,"work","offline:1",42,default);await Assert.ThrowsAsync<HttpRequestException>(()=>r.Engine.TickAsync(default));
        await using(var db=await r.Factory.CreateAsync())Assert.Equal(AgentTaskStatus.Queued,(await db.Tasks.SingleAsync()).Status);
        await r.Engine.TickAsync(default);r.Api.Verify(x=>x.SendPromptAsync(s.OpenCodeSessionId,It.IsAny<PromptRequest>(),It.IsAny<CancellationToken>()),Times.Once);
    }
}
