using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
namespace OpenCodeManager.Infrastructure;

public sealed partial class FileManager(IManagerDbFactory factory, IPathPolicy paths, OwnerGuard guard, FileStorageOptions options, TelegramOptions telegram, IAttachmentProcessor extractor, IOpenCodeClient api) : IFileManager
{
    private string Storage(string kind)
    {
        var path=Path.Combine(options.DataDirectory,kind);Directory.CreateDirectory(path);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);return path;
    }
    private static bool SecretName(string path) => path.Split(Path.DirectorySeparatorChar).Any(x=>x.StartsWith(".env",StringComparison.OrdinalIgnoreCase)||x is ".ssh" or ".gnupg" or ".aws" or ".git" or "secrets" || x.Contains("credential",StringComparison.OrdinalIgnoreCase) || x.Contains("secret",StringComparison.OrdinalIgnoreCase) || x is "auth.json" or "service.json" or ".npmrc" or ".netrc" || x.StartsWith("id_rsa",StringComparison.Ordinal) || x.EndsWith(".key",StringComparison.OrdinalIgnoreCase) || x.EndsWith(".pfx",StringComparison.OrdinalIgnoreCase) || x.EndsWith(".pem",StringComparison.OrdinalIgnoreCase));
    private string ReadPath(string path,long owner)
    {
        guard.Demand(owner);var canonical=paths.DemandAllowed(path);
        if(SecretName(canonical)||SecretName(Path.GetFullPath(path)))throw new UnauthorizedAccessException("Secret file policy denied transfer");
        if(!File.Exists(canonical))throw new FileNotFoundException();return canonical;
    }
    private async Task CheckContentAsync(Stream stream,CancellationToken ct)
    {
        // Inspect the entire byte stream in bounded windows before any transfer. This detects common plaintext credentials, not arbitrary encrypted secrets.
        var buffer=new byte[64*1024];string tail="";int count;
        while((count=await stream.ReadAsync(buffer,ct))>0)
        {
            var text=tail+Encoding.UTF8.GetString(buffer,0,count);
            if(SecretContent().IsMatch(text))throw new UnauthorizedAccessException("Credential-like content blocked by transfer policy");
            tail=text[^Math.Min(512,text.Length)..];
        }
        stream.Position=0;
    }
    [GeneratedRegex("""(?i)(-----BEGIN [^-]*PRIVATE KEY-----|[0-9]{6,12}:[A-Za-z0-9_-]{25,}|sk-[A-Za-z0-9_-]{24,}|(?:password|passwd|api[_-]?key|secret|access[_-]?token)"?\s*[=:]\s*"?[^\s",;}]{5,})""")]
    private static partial Regex SecretContent();
    public async Task<FileListing> ListAsync(string directory,int page,string? search,long owner,CancellationToken ct)
    {
        guard.Demand(owner);var dir=paths.DemandAllowed(directory);if(page is <0 or >10000)throw new ArgumentOutOfRangeException(nameof(page));
        var top=new SortedSet<string>(StringComparer.Ordinal);int capacity=(page+1)*8+1;
        foreach(var path in Directory.EnumerateFiles(dir))
        {
            ct.ThrowIfCancellationRequested();if(search is {Length:>0}&&!Path.GetFileName(path).Contains(search,StringComparison.OrdinalIgnoreCase))continue;
            try{ReadPath(path,owner);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){continue;}
            top.Add(path);if(top.Count>capacity)top.Remove(top.Max!);
        }
        var files=new List<LocalFileInfo>();foreach(var path in top.Skip(page*8).Take(8)){try{files.Add(await InspectAsync(path,owner,ct));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){ }}
        return new(files,page,top.Count>(page+1)*8);
    }
    public async Task<LocalFileInfo> InspectAsync(string path,long owner,CancellationToken ct)
    {
        var canonical=ReadPath(path,owner);var info=new FileInfo(canonical);if(info.Length>options.MaxDownloadBytes)throw new InvalidDataException("File exceeds Telegram transfer limit");
        await using var stream=new FileStream(canonical,FileMode.Open,FileAccess.Read,FileShare.Read);var header=new byte[1024*1024];int size=await stream.ReadAsync(header,ct);
        var (mime,width,height)=Detect(header.AsSpan(0,size));
        if(width is int w&&height is int h&&((long)w*h>options.MaxImagePixels||w<=0||h<=0))throw new InvalidDataException("Image exceeds pixel limit");
        return new(info.Name,canonical,info.Length,mime,width,height);
    }
    public async Task<Stream> OpenDownloadAsync(string path,long owner,CancellationToken ct)
    {
        var info=await InspectAsync(path,owner,ct);var stream=new FileStream(ReadPath(info.CanonicalPath,owner),FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,true);
        try{await CheckContentAsync(stream,ct);return stream;}catch{await stream.DisposeAsync();throw;}
    }
    private static string Name(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>150||name!=Path.GetFileName(name)||name is "." or ".."||name.Contains('\\')||name.Any(char.IsControl)||SecretName(name))throw new ArgumentException("Unsafe file name");return name;
    }
    public Task CreateDirectoryAsync(string parent,string name,long owner,CancellationToken ct)
    {
        guard.Demand(owner);ct.ThrowIfCancellationRequested();var dir=paths.DemandAllowed(parent);var target=paths.DemandAllowed(Path.Combine(dir,Name(name)));if(Directory.Exists(target)||File.Exists(target))throw new IOException("Already exists");Directory.CreateDirectory(target);return Task.CompletedTask;
    }
    public string NewUploadPath(string name,long owner){guard.Demand(owner);Name(name);return Path.Combine(Storage("uploads"),Guid.NewGuid().ToString("N")+Path.GetExtension(name));}
    private string StoredPath(string path)
    {
        var canonical=paths.Canonicalize(path);var root=paths.Canonicalize(options.DataDirectory);
        if(!canonical.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal)||SecretName(Path.GetFileName(canonical)))throw new UnauthorizedAccessException("Attachment escaped storage root");return canonical;
    }
    public async Task<FileAttachment> RegisterUploadAsync(string tempPath,string originalName,string? telegramFileId,long owner,CancellationToken ct)
    {
        guard.Demand(owner);Name(originalName);var path=StoredPath(tempPath);var info=new FileInfo(path);if(info.Length>telegram.MaxAttachmentBytes)throw new InvalidDataException("Upload too large");
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);await CheckContentAsync(stream,ct);var header=new byte[1024*1024];int read=await stream.ReadAsync(header,ct);var(mime,width,height)=Detect(header.AsSpan(0,read));
        if(header.AsSpan(0,read).StartsWith(new byte[]{0x7f,69,76,70})||header.AsSpan(0,read).StartsWith("MZ"u8))throw new InvalidDataException("Executable binary upload rejected");
        var ext=Path.GetExtension(originalName).ToLowerInvariant();
        if(ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp")
        {if(!mime.StartsWith("image/",StringComparison.Ordinal)||width is null||height is null||(long)width*height>options.MaxImagePixels)throw new InvalidDataException("Invalid or oversized image");}
        if(ext==".pdf"&&mime!="application/pdf")throw new InvalidDataException("Invalid PDF");
        if(ext is ".zip" or ".docx")
        {
            using var archive=new ZipArchive(stream,ZipArchiveMode.Read,true);
            if(archive.Entries.Count>options.MaxArchiveFiles||archive.Entries.Sum(x=>x.Length)>options.MaxArchiveBytes)throw new InvalidDataException("Archive exceeds limits");
            if(ext==".docx"&&archive.GetEntry("word/document.xml") is null)throw new InvalidDataException("Invalid DOCX");
            mime=ext==".docx"?"application/vnd.openxmlformats-officedocument.wordprocessingml.document":"application/zip";
        }
        if(mime=="application/octet-stream"&&read>0&&!header.AsSpan(0,read).Contains((byte)0))mime="text/plain";
        var file=new FileAttachment{OriginalName=originalName,LocalPath=path,MimeType=mime,Size=info.Length,TelegramFileId=telegramFileId,Width=width,Height=height};
        await using var db=await factory.CreateAsync(ct);db.Attachments.Add(file);await db.SaveChangesAsync(ct);return file;
    }
    public async Task SaveUploadAsync(Guid attachmentId,string directory,bool overwrite,long owner,CancellationToken ct)
    {
        guard.Demand(owner);await using var db=await factory.CreateAsync(ct);var f=await db.Attachments.SingleAsync(x=>x.Id==attachmentId,ct);var source=StoredPath(f.LocalPath);var dir=paths.DemandAllowed(directory);
        if(!Directory.Exists(dir))throw new DirectoryNotFoundException();var target=paths.DemandAllowed(Path.Combine(dir,Name(f.OriginalName)));if(SecretName(target))throw new UnauthorizedAccessException();
        var stage=Path.Combine(dir,".manager-upload-"+Guid.NewGuid().ToString("N"));
        try
        {
            await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            {await CheckContentAsync(input,ct);await using var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None);await input.CopyToAsync(output,ct);await output.FlushAsync(ct);}
            paths.DemandAllowed(dir);paths.DemandAllowed(target);File.Move(stage,target,overwrite);
            db.Transfers.Add(new(){AttachmentId=f.Id,Direction="SaveToProject",LocalPath=target,Status="Completed",CompletedAtUtc=DateTime.UtcNow});await db.SaveChangesAsync(ct);
        }
        finally{if(File.Exists(stage))File.Delete(stage);}
    }
    public async Task DeleteUploadAsync(Guid attachmentId,long owner,CancellationToken ct)
    {
        guard.Demand(owner);await using var db=await factory.CreateAsync(ct);var f=await db.Attachments.SingleAsync(x=>x.Id==attachmentId,ct);
        if(await db.TaskFiles.AnyAsync(x=>x.AttachmentId==attachmentId,ct))throw new InvalidOperationException("Attachment is referenced by a task");
        File.Delete(StoredPath(f.LocalPath));db.Attachments.Remove(f);await db.SaveChangesAsync(ct);
    }
    public async Task<FileAttachment> ImportProjectFileAsync(string path,long owner,CancellationToken ct)
    {
        var info=await InspectAsync(path,owner,ct);if(info.Size>telegram.MaxAttachmentBytes)throw new InvalidDataException("Attachment too large");var target=NewUploadPath(info.Name,owner);
        try{await using(var input=await OpenDownloadAsync(path,owner,ct)){await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);await input.CopyToAsync(output,ct);}return await RegisterUploadAsync(target,info.Name,null,owner,ct);}
        catch{File.Delete(target);throw;}
    }
    public async Task<string> CreateArchiveAsync(string? directory,IReadOnlyList<string>? files,long owner,CancellationToken ct)
    {
        guard.Demand(owner);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(options.MaxArchiveSeconds));ct=timeout.Token;
        var baseDir=directory is null?null:paths.DemandAllowed(directory);var outputPath=Path.Combine(Storage("downloads"),Guid.NewGuid()+".zip");
        int count=0;long bytes=0;
        try
        {
            using(var archive=new ZipArchive(new FileStream(outputPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None),ZipArchiveMode.Create))
            {
                var pending=new Stack<(string Path,int Depth)>(); int directoryCount=0;if(baseDir is not null)pending.Push((baseDir,0));
                var candidates=new List<string>();
                if(files is not null)candidates.AddRange(files);
                while(pending.TryPop(out var dir))
                {
                    ct.ThrowIfCancellationRequested();if(dir.Depth>options.MaxArchiveDepth)throw new InvalidDataException("Archive depth limit exceeded");
                    foreach(var file in Directory.EnumerateFiles(paths.DemandAllowed(dir.Path)))
                    {if(new FileInfo(file).LinkTarget is null&&!SecretName(file)){candidates.Add(file);if(candidates.Count>options.MaxArchiveFiles)throw new InvalidDataException("Archive file count limit exceeded");}}
                    foreach(var sub in Directory.EnumerateDirectories(dir.Path))
                    {
                        ct.ThrowIfCancellationRequested(); if(++directoryCount>options.MaxArchiveFiles) throw new InvalidDataException("Archive directory count limit exceeded");
                        if(new DirectoryInfo(sub).LinkTarget is not null||SecretName(sub)||Path.GetFileName(sub) is "node_modules" or "bin" or "obj" or "target" or ".venv")continue;
                        try{pending.Push((paths.DemandAllowed(sub),dir.Depth+1));}catch(UnauthorizedAccessException){ }
                    }
                }
                var names=new HashSet<string>(StringComparer.Ordinal);
                foreach(var candidate in candidates.Distinct())
                {
                    ct.ThrowIfCancellationRequested();LocalFileInfo info;
                    try{info=await InspectAsync(candidate,owner,ct);}catch(UnauthorizedAccessException){continue;}
                    bytes+=info.Size;if(bytes>options.MaxArchiveBytes||++count>options.MaxArchiveFiles)throw new InvalidDataException("Archive exceeds transfer limits");
                    string entryName=baseDir is not null?Path.GetRelativePath(baseDir,info.CanonicalPath):info.Name;
                    if(entryName.StartsWith("..",StringComparison.Ordinal)||Path.IsPathRooted(entryName))throw new UnauthorizedAccessException();
                    if(!names.Add(entryName))entryName=count+"-"+info.Name;
                    Stream input;try{input=await OpenDownloadAsync(info.CanonicalPath,owner,ct);}catch(UnauthorizedAccessException){continue;}
                    await using(input){var entry=archive.CreateEntry(entryName,CompressionLevel.Fastest);await using var output=entry.Open();await input.CopyToAsync(output,ct);}
                }
            }
            if(new FileInfo(outputPath).Length>options.MaxDownloadBytes)throw new InvalidDataException("ZIP exceeds Telegram limit");return outputPath;
        }
        catch{File.Delete(outputPath);throw;}
    }
    public async Task AddToNextTaskAsync(IEnumerable<Guid> attachments,long owner,CancellationToken ct)
    {
        guard.Demand(owner);await using var db=await factory.CreateAsync(ct);var nav=await db.Navigation.SingleAsync(x=>x.UserId==owner,ct);
        var ids=JsonSerializer.Deserialize<List<Guid>>(nav.PendingFileIdsJson??"[]")??[];
        foreach(var id in attachments){if(!await db.Attachments.AnyAsync(x=>x.Id==id&&x.ExpiresAtUtc>DateTime.UtcNow,ct))throw new FileNotFoundException();if(!ids.Contains(id))ids.Add(id);}
        if(ids.Count>10)throw new InvalidDataException("Maximum 10 attachments per task");nav.PendingFileIdsJson=JsonSerializer.Serialize(ids);await db.SaveChangesAsync(ct);
    }
    public async Task<List<OpenCodeFileInput>> PrepareTaskFilesAsync(Guid taskId,string directory,string? provider,string? model,CancellationToken ct)
    {
        await using var db=await factory.CreateAsync(ct);var ids=await db.TaskFiles.Where(x=>x.TaskId==taskId).Select(x=>x.AttachmentId).ToListAsync(ct);var files=await db.Attachments.Where(x=>ids.Contains(x.Id)).ToListAsync(ct);
        if(files.Any(x=>x.MimeType.StartsWith("image/",StringComparison.Ordinal)))
        {
            var catalog=await api.GetModelsAsync(directory,ct);
            var selected=catalog.SingleOrDefault(x=>x.ProviderId==provider&&x.Id==model);
            if(selected?.Capabilities?.Input.Any(x=>x.Contains("image",StringComparison.OrdinalIgnoreCase))!=true)throw new AttachmentPreparationException("У выбранной модели не заявлен image input. Выберите модель для изображений: "+string.Join(", ",catalog.Where(x=>x.Enabled&&x.Capabilities?.Input.Any(i=>i.Contains("image",StringComparison.OrdinalIgnoreCase))==true).Take(10).Select(x=>x.ProviderId+"/"+x.Id)));
        }
        var result=new List<OpenCodeFileInput>();long total=0;
        foreach(var f in files)
        {
            var path=StoredPath(f.LocalPath);total+=f.Size;if(total>telegram.MaxAttachmentBytes*2)throw new InvalidDataException("Task attachment limit exceeded");
            if(f.MimeType.StartsWith("image/",StringComparison.Ordinal)||f.MimeType=="application/pdf")
            {var data=await File.ReadAllBytesAsync(path,ct);result.Add(new("data:"+f.MimeType+";base64,"+Convert.ToBase64String(data),f.OriginalName));}
            else
            {
                var ext=Path.GetExtension(f.OriginalName).ToLowerInvariant();
                if(f.MimeType=="application/zip"||f.MimeType=="application/octet-stream")throw new InvalidDataException("Binary/ZIP attachments can be saved or downloaded; automatic extraction for AI is disabled");
                var text=await extractor.ExtractAsync(path,ext,ct);result.Add(new("data:text/plain;base64,"+Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),f.OriginalName));
            }
        }
        return result;
    }
    public async Task CleanupAsync(CancellationToken ct)
    {
        await using var db=await factory.CreateAsync(ct);var referenced=await db.TaskFiles.Select(x=>x.AttachmentId).ToListAsync(ct);
        foreach(var f in await db.Attachments.Where(x=>x.ExpiresAtUtc<DateTime.UtcNow&&!referenced.Contains(x.Id)).ToListAsync(ct)){File.Delete(StoredPath(f.LocalPath));db.Attachments.Remove(f);}
        await db.Transfers.Where(x=>x.Status=="Pending"||x.Status=="Running").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"NeedsReconciliation").SetProperty(x=>x.Error,"Restart interrupted transfer"),ct);
        await db.SaveChangesAsync(ct);
        foreach(var path in Directory.EnumerateFiles(Storage("downloads")))if(File.GetLastWriteTimeUtc(path)<DateTime.UtcNow.AddHours(-1))File.Delete(path);
    }
    private static (string Mime,int? Width,int? Height) Detect(ReadOnlySpan<byte> h)
    {
        if(h.Length>=24&&h.StartsWith(new byte[]{137,80,78,71,13,10,26,10}))return("image/png",BinaryPrimitives.ReadInt32BigEndian(h[16..20]),BinaryPrimitives.ReadInt32BigEndian(h[20..24]));
        if(h.Length>=10&&(h.StartsWith("GIF87a"u8)||h.StartsWith("GIF89a"u8)))return("image/gif",BinaryPrimitives.ReadUInt16LittleEndian(h[6..8]),BinaryPrimitives.ReadUInt16LittleEndian(h[8..10]));
        if(h.Length>=30&&h[..4].SequenceEqual("RIFF"u8)&&h[8..12].SequenceEqual("WEBP"u8))
        {
            if(h[12..16].SequenceEqual("VP8X"u8))return("image/webp",1+h[24]+(h[25]<<8)+(h[26]<<16),1+h[27]+(h[28]<<8)+(h[29]<<16));
            if(h[12..16].SequenceEqual("VP8 "u8))return("image/webp",BinaryPrimitives.ReadUInt16LittleEndian(h[26..28])&0x3fff,BinaryPrimitives.ReadUInt16LittleEndian(h[28..30])&0x3fff);
            if(h[12..16].SequenceEqual("VP8L"u8)){int bits=BinaryPrimitives.ReadInt32LittleEndian(h[21..25]);return("image/webp",1+(bits&0x3fff),1+((bits>>14)&0x3fff));}
        }
        if(h.Length>=3&&h[0]==0xff&&h[1]==0xd8)
        {
            for(int pos=2;pos+9<h.Length;){if(h[pos++]!=0xff)continue;byte marker=h[pos++];if(marker is 0xd8 or 0xd9)continue;if(pos+2>h.Length)break;int length=BinaryPrimitives.ReadUInt16BigEndian(h[pos..(pos+2)]);if(length<2||pos+length>h.Length)break;if(marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)return("image/jpeg",BinaryPrimitives.ReadUInt16BigEndian(h[(pos+5)..(pos+7)]),BinaryPrimitives.ReadUInt16BigEndian(h[(pos+3)..(pos+5)]));pos+=length;}
            return("image/jpeg",null,null);
        }
        if(h.StartsWith("%PDF-"u8))return("application/pdf",null,null);return("application/octet-stream",null,null);
    }
}
