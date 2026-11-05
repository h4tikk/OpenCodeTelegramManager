using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCodeManager.Infrastructure;

namespace OpenCodeManager.IntegrationTests;
public sealed class FileTests
{
    private static FileManager Files(TestRig r, long limit = 40 * 1024 * 1024) => r.CreateFiles(limit);
    [Fact]
    public async Task DownloadsAreStreamedAndSecretContentCannotBeDisguised()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var files = Files(r);
        var path = Path.Combine(r.Root, "report.txt");
        await File.WriteAllTextAsync(path, "ordinary text");
        await using (var stream = await files.OpenDownloadAsync(path, 42, default))
        {
            Assert.IsType<FileStream>(stream);
            Assert.Equal("ordinary text", await new StreamReader(stream).ReadToEndAsync());
        }

        await File.WriteAllTextAsync(path, "password=supersecret");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.OpenDownloadAsync(path, 42, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.OpenDownloadAsync(path, 7, default));
    }

    [Fact]
    public async Task UploadSaveIsAtomicAndRequiresExplicitOverwrite()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var files = Files(r);
        var upload = files.NewUploadPath("notes.txt", 42);
        await File.WriteAllTextAsync(upload, "new content");
        var f = await files.RegisterUploadAsync(upload, "notes.txt", null, 42, default);
        var dir = Path.Combine(r.Root, "repo");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "notes.txt");
        await File.WriteAllTextAsync(target, "old");
        await Assert.ThrowsAsync<IOException>(() => files.SaveUploadAsync(f.Id, dir, false, 42, default));
        Assert.Equal("old", await File.ReadAllTextAsync(target));
        await files.SaveUploadAsync(f.Id, dir, true, 42, default);
        Assert.Equal("new content", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(dir, ".manager-upload-*"));
        Assert.Throws<ArgumentException>(() => files.NewUploadPath("../escape.txt", 42));
    }

    [Fact]
    public async Task ZipExcludesSecretsBuildOutputsAndAllSymbolicLinks()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var dir = Path.Combine(r.Root, "project");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "README.md"), "hello");
        await File.WriteAllTextAsync(Path.Combine(dir, ".env"), "PASSWORD=hidden");
        await File.WriteAllTextAsync(Path.Combine(dir, "disguised.txt"), "api_key=abcdef123456");
        Directory.CreateSymbolicLink(Path.Combine(dir, "outside"), "/etc");
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        await File.WriteAllTextAsync(Path.Combine(dir, "bin/out.txt"), "build output");
        var zip = await Files(r).CreateArchiveAsync(dir, null, 42, default);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "README.md" }, archive.Entries.Select(x => x.FullName));
    }

    [Fact]
    public async Task ZipSizeLimitRemovesPartialArchive()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var dir = Path.Combine(r.Root, "project");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "README.md"), "larger than limit");
        await Assert.ThrowsAsync<InvalidDataException>(() => Files(r, 4).CreateArchiveAsync(dir, null, 42, default));
        Assert.Empty(Directory.GetFiles(Path.Combine(r.Root, "data/downloads")));
    }

    [Fact]
    public async Task ImageHeaderIsValidatedAndAttachedUsingActualV2DataUri()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var files = Files(r);
        var path = files.NewUploadPath("screenshot.png", 42);
        await File.WriteAllBytesAsync(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));
        var file = await files.RegisterUploadAsync(path, "screenshot.png", null, 42, default);
        Assert.Equal(1, file.Width);
        Assert.Equal(1, file.Height);
        Assert.Equal("image/png", file.MimeType);
        var s = await r.SessionAsync("repo");
        await r.Navigator.NavigationAsync(42, default);
        await files.AddToNextTaskAsync([file.Id], 42, default);
        var t = await r.Tasks.EnqueueAsync(s.Id, "analyze", "u", 42, default);
        r.Api.Setup(x => x.GetModelsAsync(s.WorkingDirectory, It.IsAny<CancellationToken>())).ReturnsAsync([new("vision", "provider", "Vision", true, "active", new(["text", "image"]))]);
        var payload = await files.PrepareTaskFilesAsync(t.Id, s.WorkingDirectory, "provider", "vision", default);
        Assert.Single(payload);
        Assert.StartsWith("data:image/png;base64,", payload[0].Uri);
        await using (var db = await r.Factory.CreateAsync())
        {
            Assert.Single(await db.TaskFiles.ToListAsync());
            Assert.Null((await db.Navigation.SingleAsync()).PendingFileIdsJson);
        }
    }

    [Fact]
    public async Task RenamedExecutableAndFakeImageAreRejected()
    {
        await using var r = new TestRig();
        await r.InitializeAsync();
        var files = Files(r);
        var a = files.NewUploadPath("fake.png", 42);
        await File.WriteAllTextAsync(a, "not an image");
        await Assert.ThrowsAsync<InvalidDataException>(() => files.RegisterUploadAsync(a, "fake.png", null, 42, default));
        var b = files.NewUploadPath("innocent.txt", 42);
        await File.WriteAllBytesAsync(b, [0x7f, 69, 76, 70, 0, 0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => files.RegisterUploadAsync(b, "innocent.txt", null, 42, default));
    }
}
