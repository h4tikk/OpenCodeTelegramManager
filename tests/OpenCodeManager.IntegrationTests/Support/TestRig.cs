using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.IntegrationTests;

public sealed class TestRig : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "manager-test-" + Guid.NewGuid());
    public TestFactory Factory { get; }
    public Mock<IOpenCodeClient> Api { get; } = new(MockBehavior.Strict);
    public NavigationService Navigator { get; }
    public ProjectManager Projects { get; }
    public SessionManager Sessions { get; }
    public TaskManager Tasks { get; }
    public PermissionManager Permissions { get; }
    public TaskEngine Engine { get; }
    public PathPolicy Paths { get; }
    public DirectoryNavigation Navigation { get; }
    public FileSystemBrowserService Browser { get; }

    public TestRig()
    {
        Directory.CreateDirectory(Root);
        Factory = new(Path.Combine(Root, "state.db"));
        var options = new ProjectOptions
        {
            AllowedRoots = [Root]
        };
        Paths = new(options);
        var guard = new OwnerGuard(new() { OwnerUserId = 42 });
        Navigator = new(Factory, guard);
        Projects = new(Factory, Paths, guard, NullLogger<ProjectManager>.Instance,new ProjectTechnologyDetector(Paths));
        Sessions = new(Factory, Api.Object, Paths, guard, Projects, NullLogger<SessionManager>.Instance);
        Tasks = new(Factory, Api.Object, Paths, guard, NullLogger<TaskManager>.Instance);
        Permissions = new(Factory, Api.Object, Paths, guard, NullLogger<PermissionManager>.Instance);
        var execution = new TaskExecutionOptions
        {
            MaxConcurrentTasks = 2
        };
        var gate = new TaskExecutionGate();
        var dispatcher = new TaskDispatcher(Factory, Api.Object, Paths, execution, NullLogger<TaskDispatcher>.Instance);
        var reconciler = new TaskReconciler(Factory, Api.Object, Paths, Permissions, execution, NullLogger<TaskReconciler>.Instance);
        var recorder = new TaskEventRecorder(Factory, execution, gate);
        Engine = new(Factory, dispatcher, reconciler, recorder, gate, execution, NullLogger<TaskEngine>.Instance);
        Navigation = new(Factory, Paths, options, guard);
        Browser = new(Paths, Projects, Sessions, Navigation, Factory, guard,new ProjectTechnologyDetector(Paths));
    }

    public async Task InitializeAsync()
    {
        await using var db = new ManagerDbContext(ManagerDbContext.Options(Path.Combine(Root, "state.db")));
        await DatabaseInitializer.InitializeAsync(db);
    }

    public async Task<AgentSession> SessionAsync(string name)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        await using var db = await Factory.CreateAsync();
        var p = new Project
        {
            Name = name,
            AbsolutePath = dir
        };
        db.Projects.Add(p);
        var s = new AgentSession
        {
            ProjectId = p.Id,
            WorkingDirectory = dir,
            OpenCodeSessionId = "ses_" + name,
            Title = name
        };
        db.Sessions.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public static OpenCodeSession Remote(AgentSession s) => new(s.OpenCodeSessionId, "proj", s.Title, new(s.WorkingDirectory), new(1, 1, null, null, null), null, null, null, null);
    public void IdleApi(AgentSession s)
    {
        Api.Setup(x => x.GetSessionAsync(s.OpenCodeSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(Remote(s));
        Api.Setup(x => x.GetInboxAsync(s.OpenCodeSessionId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Api.Setup(x => x.GetPermissionsAsync(s.OpenCodeSessionId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Api.Setup(x => x.GetMessagesAsync(s.OpenCodeSessionId, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ApiPage<OpenCodeMessage>([], null));
        Api.Setup(x => x.UpdateSessionAsync(s.OpenCodeSessionId, null, null, It.IsAny<List<PermissionRule>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    public FileManager CreateFiles(long limit = 40 * 1024 * 1024)
    {
        var options = new FileStorageOptions
        {
            DataDirectory = Path.Combine(Root, "data"),
            MaxArchiveBytes = limit
        };
        var guard = new OwnerGuard(new() { OwnerUserId = 42 });
        var telegram = new TelegramOptions();
        var policy = new FileAccessPolicy(Paths, guard, options);
        var storage = new FileStoragePaths(options);
        var local = new LocalFileService(Paths, guard, options, policy);
        var attachments = new AttachmentStorageService(Factory, Paths, guard, options, telegram, policy, storage, local);
        var archives = new ArchiveService(local, storage, Paths, guard, options);
        var taskFiles = new TaskAttachmentFactory(Factory, policy, telegram, new AttachmentProcessor(telegram), Api.Object);
        var selection = new AttachmentSelectionService(Factory, guard);
        return new(local, attachments, archives, taskFiles, selection);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Root, true);
        return ValueTask.CompletedTask;
    }
}
