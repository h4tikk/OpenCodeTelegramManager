using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using OpenCodeManager.Domain;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
namespace OpenCodeManager.Telegram;

public sealed partial class TelegramInterface
{
    private static List<string> Selected(NavigationState nav)=>JsonSerializer.Deserialize<List<string>>(nav.SelectedFilesJson??"[]")??[];
    private async Task<bool> HandleFileInputAsync(NavigationState nav,string input,CancellationToken ct)
    {
        if(nav.InputMode=="mkdir"&&nav.BrowserPath is not null){await fileManager.CreateDirectoryAsync(nav.BrowserPath,input,security.OwnerUserId,ct);nav.InputMode=null;await manager.SaveNavigationAsync(nav,ct);await BrowseAsync(nav.BrowserPath,0,nav.BrowserMessageId,ct);return true;}
        if(nav.InputMode=="search"&&nav.BrowserPath is not null){nav.InputMode=null;await manager.SaveNavigationAsync(nav,ct);await FileListAsync(0,input,nav.BrowserMessageId,ct);return true;}
        return false;
    }
    private async Task ReceiveFileAsync(Message msg,int updateId,CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        string fileId,name;long size;
        if(msg.Document is { } document){fileId=document.FileId;name=document.FileName??"document.bin";size=document.FileSize??0;}
        else{var photo=msg.Photo!.OrderByDescending(x=>x.Width*x.Height).First();fileId=photo.FileId;name="telegram-photo-"+updateId+".jpg";size=photo.FileSize??0;}
        if(size>options.MaxAttachmentBytes)throw new InvalidDataException("Attachment exceeds upload limit");
        var path=fileManager.NewUploadPath(name,security.OwnerUserId);var transfer=await StartTransferAsync("Upload",null,path,ct);
        try
        {
            var file=await bot.GetFile(fileId,ct);if(file.FileSize>options.MaxAttachmentBytes)throw new InvalidDataException("Attachment exceeds upload limit");
            await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                using var limited=new LimitedWriteStream(output,options.MaxAttachmentBytes);await bot.DownloadFile(file.FilePath!,limited,ct);
            }
            var attachment=await fileManager.RegisterUploadAsync(path,name,fileId,security.OwnerUserId,ct);await FinishTransferAsync(transfer.Id,null,ct,attachment.Id);
            if(!string.IsNullOrWhiteSpace(msg.Caption))
            {
                await fileManager.AddToNextTaskAsync([attachment.Id],security.OwnerUserId,ct);var nav=await manager.NavigationAsync(security.OwnerUserId,ct);await SubmitAsync(nav,msg.Caption,updateId,ct);
            }
            else await UploadCardAsync(attachment.Id,ct);
        }
        catch(Exception e){File.Delete(path);await FinishTransferAsync(transfer.Id,e.GetType().Name,CancellationToken.None);throw;}
    }
    private async Task UploadCardAsync(Guid id,CancellationToken ct)
    {
        await using var db=await factory.CreateAsync(ct);var file=await db.Attachments.SingleAsync(x=>x.Id==id,ct);
        await SendTextAsync($"Получен файл: {file.OriginalName}\n{file.Size:N0} bytes\n{file.MimeType}\n{file.Width} × {file.Height}\nСохранён во временном хранилище.",new(new[]{new[]{B("Добавить к задаче","uattach",id.ToString("N")),B("Сохранить в проект","usave",id.ToString("N"))},new[]{B("Выбрать папку","files","0"),B("Удалить временный","udelete",id.ToString("N"))},new[]{B("Оригинал","udownload",id.ToString("N")),B("Меню","home","0")}}),ct);
    }
    private async Task<bool> FileCallbackAsync(string action,string argument,int messageId,CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);var nav=await manager.NavigationAsync(security.OwnerUserId,ct);
        switch(action)
        {
            case "flist":await FileListAsync(Page(argument),null,messageId,ct);return true;
            case "fsearch":nav.InputMode="search";await manager.SaveNavigationAsync(nav,ct);await SendTextAsync("Введите часть имени файла. Поиск в текущей папке, без рекурсивного обхода.",null,ct);return true;
            case "fmkdir":nav.InputMode="mkdir";await manager.SaveNavigationAsync(nav,ct);await SendTextAsync("Введите имя новой папки (один компонент пути).",null,ct);return true;
            case "file":case "fdownload":case "fpreview":case "fattach":case "fselect":
                if(!choices.TryGetValue(argument,out var choice)||choice.Kind!="file"||choice.Expires<DateTime.UtcNow){await SendTextAsync("Выбор устарел. Обновите список файлов.",null,ct);return true;}
                if(action=="file")await FileCardAsync(choice.Value,messageId,ct);
                if(action=="fdownload")await DownloadProjectFileAsync(choice.Value,false,ct);
                if(action=="fpreview")
                {
                    var info=await fileManager.InspectAsync(choice.Value,security.OwnerUserId,ct);
                    if(info.MimeType.StartsWith("image/",StringComparison.Ordinal))await DownloadProjectFileAsync(choice.Value,true,ct);
                    else{await using var stream=await fileManager.OpenDownloadAsync(choice.Value,security.OwnerUserId,ct);using var reader=new StreamReader(stream,Encoding.UTF8,true,4096,true);var buffer=new char[100_000];int read=await reader.ReadAsync(buffer,ct);var text=new string(buffer,0,read);if(text.Contains('\0'))throw new InvalidDataException("Binary preview unavailable");await SendTextAsync(text,null,ct);}
                }
                if(action=="fattach"){var file=await fileManager.ImportProjectFileAsync(choice.Value,security.OwnerUserId,ct);await fileManager.AddToNextTaskAsync([file.Id],security.OwnerUserId,ct);await SendTextAsync("Файл добавлен к следующей задаче. Выберите модель с image input для изображений.",Menu,ct);}
                if(action=="fselect")
                {var selected=Selected(nav);if(!selected.Remove(choice.Value))selected.Add(choice.Value);if(selected.Count>20)throw new InvalidDataException("Maximum 20 selected files");nav.SelectedFilesJson=JsonSerializer.Serialize(selected);await manager.SaveNavigationAsync(nav,ct);await FileListAsync(0,null,messageId,ct);}
                return true;
            case "fclear":nav.SelectedFilesJson=null;await manager.SaveNavigationAsync(nav,ct);await FileListAsync(0,null,messageId,ct);return true;
            case "fselected":foreach(var file in Selected(nav))await DownloadProjectFileAsync(file,false,ct);return true;
            case "fattachsel":
                foreach(var path in Selected(nav)){var file=await fileManager.ImportProjectFileAsync(path,security.OwnerUserId,ct);await fileManager.AddToNextTaskAsync([file.Id],security.OwnerUserId,ct);}await SendTextAsync("Выбранные файлы добавлены к следующей задаче.",Menu,ct);return true;
            case "fzipdir":case "fzipsel":
                var archive=await fileManager.CreateArchiveAsync(action=="fzipdir"?nav.BrowserPath:null,action=="fzipsel"?Selected(nav):null,security.OwnerUserId,ct);
                try{await SendStoredFileAsync(archive,"files.zip","application/zip",false,null,ct);}finally{System.IO.File.Delete(archive);}return true;
            case "uattach":case "usave":case "uoverwrite":case "udelete":case "udownload":
                if(!Guid.TryParseExact(argument,"N",out var uploadId))return true;
                if(action=="uattach"){await fileManager.AddToNextTaskAsync([uploadId],security.OwnerUserId,ct);await SendTextAsync("Вложение добавлено. Напишите задачу в выбранной сессии.",Menu,ct);}
                if(action=="usave"||action=="uoverwrite")
                {
                    if(nav.BrowserPath is null){await SendTextAsync("Выберите папку в файловом менеджере, затем снова нажмите «Сохранить в проект» у вложения.",Menu,ct);return true;}
                    if(action=="usave")
                    {
                        await using var db=await factory.CreateAsync(ct);var f=await db.Attachments.SingleAsync(x=>x.Id==uploadId,ct);
                        if(System.IO.File.Exists(Path.Combine(nav.BrowserPath,f.OriginalName)))
                        {
                            var key=StoreChoice("overwrite",nav.BrowserPath,uploadId.ToString("N"),null);
                            await SendTextAsync("Файл уже существует в выбранной папке. Подтвердите перезапись: "+nav.BrowserPath+"/"+f.OriginalName,new(new[]{new[]{B("Перезаписать","confirmwrite",key),B("Отмена","home","0")}}),ct);return true;
                        }
                    }
                    // Direct uoverwrite callbacks are deliberately rejected; overwrite requires the one-use scoped confirmation.
                    if(action=="uoverwrite")return true;
                    await fileManager.SaveUploadAsync(uploadId,nav.BrowserPath,false,security.OwnerUserId,ct);await SendTextAsync("Файл сохранён в выбранной папке.",Menu,ct);
                }
                if(action=="udelete"){await fileManager.DeleteUploadAsync(uploadId,security.OwnerUserId,ct);await SendTextAsync("Временный файл удалён.",Menu,ct);}
                if(action=="udownload")
                {await using var db=await factory.CreateAsync(ct);var f=await db.Attachments.SingleAsync(x=>x.Id==uploadId,ct);await SendStoredFileAsync(f.LocalPath,f.OriginalName,f.MimeType,false,f.Id,ct);}
                return true;
            case "confirmwrite":
                if(!choices.TryRemove(argument,out var confirmation)||confirmation.Kind!="overwrite"||confirmation.Expires<DateTime.UtcNow||!Guid.TryParseExact(confirmation.Provider,"N",out var confirmedId))return true;
                await fileManager.SaveUploadAsync(confirmedId,confirmation.Value,true,security.OwnerUserId,ct);await SendTextAsync("Файл заменён после подтверждения.",Menu,ct);return true;
            case "resultfiles":
                if(!Guid.TryParseExact(argument,"N",out var taskId))return true;
                await ResultFilesAsync(taskId,messageId,ct);return true;
            case "sessiontask":
                if(Guid.TryParseExact(argument,"N",out var continueId)){await using var db=await factory.CreateAsync(ct);var task=await db.Tasks.SingleAsync(x=>x.Id==continueId,ct);await SessionAsync(task.SessionId,ct);}return true;
            default:return false;
        }
    }
    private async Task FileListAsync(int page,string? search,int? messageId,CancellationToken ct)
    {
        var nav=await manager.NavigationAsync(security.OwnerUserId,ct);if(nav.BrowserPath is null){await BrowserRootsAsync(messageId,ct);return;}
        if(search is not null) {nav.FileSearchQuery=search;await manager.SaveNavigationAsync(nav,ct);}
        var listing=await fileManager.ListAsync(nav.BrowserPath,page,nav.FileSearchQuery,security.OwnerUserId,ct);var selected=Selected(nav);var rows=new List<InlineKeyboardButton[]>();
        foreach(var f in listing.Files)
        {var key=StoreChoice("file",f.CanonicalPath,null,null);rows.Add([B(f.Name,"file",key),B(selected.Contains(f.CanonicalPath)?"[x]":"[ ]","fselect",key)]);}
        if(page>0)rows.Add([B("←","flist",(page-1).ToString())]);if(listing.HasMore)rows.Add([B("→","flist",(page+1).ToString())]);
        rows.Add([B("Поиск","fsearch","0"),B("Скачать папку ZIP","fzipdir","0")]);
        if(selected.Count>0){rows.Add([B("Скачать выбранные","fselected","0"),B("ZIP выбранных","fzipsel","0")]);rows.Add([B("Добавить к задаче","fattachsel","0"),B("Очистить","fclear","0")]);}
        rows.Add([B("Папки","fpage","0"),B("Корни","files","0")]);
        await RenderBrowserAsync(nav.BrowserPath+"\nФайлы. Выбрано: "+selected.Count+"\nСекреты исключаются политикой передачи.",new(rows),messageId,ct);
    }
    private async Task FileCardAsync(string path,int? messageId,CancellationToken ct)
    {
        var f=await fileManager.InspectAsync(path,security.OwnerUserId,ct);var key=StoreChoice("file",f.CanonicalPath,null,null);
        await RenderBrowserAsync($"{f.Name}\nРазмер: {f.Size:N0} bytes\nТип: {f.MimeType}\n{f.Width} × {f.Height}",new(new[]{new[]{B("Скачать оригинал","fdownload",key),B("Предпросмотр","fpreview",key)},new[]{B("Отправить агенту","fattach",key),B("Выбрать","fselect",key)},new[]{B("Назад","flist","0")}}),messageId,ct);
    }
    private async Task DownloadProjectFileAsync(string path,bool preview,CancellationToken ct)
    {
        var info=await fileManager.InspectAsync(path,security.OwnerUserId,ct);await using var stream=await fileManager.OpenDownloadAsync(path,security.OwnerUserId,ct);
        var transfer=await StartTransferAsync("Download",null,info.CanonicalPath,ct);
        try{await SendStreamAsync(stream,info.Name,info.MimeType,preview,ct);await FinishTransferAsync(transfer.Id,null,ct);}catch(Exception e){await FinishTransferAsync(transfer.Id,e.GetType().Name,CancellationToken.None);throw;}
    }
    private async Task SendStoredFileAsync(string path,string name,string mime,bool preview,Guid? attachment,CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);var transfer=await StartTransferAsync("Download",attachment,path,ct);
        try{await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);await SendStreamAsync(stream,name,mime,preview,ct);await FinishTransferAsync(transfer.Id,null,ct);}catch(Exception e){await FinishTransferAsync(transfer.Id,e.GetType().Name,CancellationToken.None);throw;}
    }
    private async Task SendStreamAsync(Stream stream,string name,string mime,bool preview,CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);await outgoing.WaitAsync(ct);
        try
        {
            var file=InputFile.FromStream(stream,name);
            if(preview&&mime=="image/gif")await bot.SendAnimation(Chat,file,caption:name,cancellationToken:ct);
            else if(preview&&mime is "image/png" or "image/jpeg" or "image/webp")await bot.SendPhoto(Chat,file,caption:name,cancellationToken:ct);
            else await bot.SendDocument(Chat,file,caption:name,cancellationToken:ct);
            await Task.Delay(1100,ct);
        }
        finally{outgoing.Release();}
    }
    private async Task<FileTransfer> StartTransferAsync(string direction,Guid? attachment,string path,CancellationToken ct)
    {await using var db=await factory.CreateAsync(ct);var transfer=new FileTransfer{Direction=direction,AttachmentId=attachment,LocalPath=path,Status="Running"};db.Transfers.Add(transfer);await db.SaveChangesAsync(ct);return transfer;}
    private async Task FinishTransferAsync(Guid id,string? error,CancellationToken ct,Guid? attachment=null)
    {await using var db=await factory.CreateAsync(ct);var t=await db.Transfers.SingleAsync(x=>x.Id==id,ct);t.Status=error is null?"Completed":"Failed";t.Error=error;t.AttachmentId??=attachment;t.CompletedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync(ct);}
    private async Task ResultFilesAsync(Guid taskId,int? messageId,CancellationToken ct)
    {
        await using var db=await factory.CreateAsync(ct);var task=await db.Tasks.SingleAsync(x=>x.Id==taskId,ct);var session=await db.Sessions.SingleAsync(x=>x.Id==task.SessionId,ct);var attempt=await db.Attempts.Where(x=>x.TaskId==taskId).OrderByDescending(x=>x.AttemptNumber).FirstOrDefaultAsync(ct);
        if(attempt?.OpenCodeMessageId is null){await SendTextAsync("Нет принятой попытки выполнения.",null,ct);return;}
        var changed=await api.GetDiffAsync(session.OpenCodeSessionId,attempt.OpenCodeMessageId,ct);var rows=new List<InlineKeyboardButton[]>();
        foreach(var item in changed.Take(30))
        {
            try{var info=await fileManager.InspectAsync(Path.Combine(session.WorkingDirectory,item.File),security.OwnerUserId,ct);rows.Add([B(info.Name,"file",StoreChoice("file",info.CanonicalPath,null,null))]);}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){ }
        }
        rows.Add([B("Карточка задачи","task",taskId.ToString("N"))]);
        await RenderBrowserAsync("Файлы из снимков OpenCode, проверенные на диске. Выберите конкретные файлы для скачивания или ZIP; изменения пользователя автоматически не включаются.",new(rows),messageId,ct);
    }
}
