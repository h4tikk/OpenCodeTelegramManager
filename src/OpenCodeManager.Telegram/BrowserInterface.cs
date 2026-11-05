using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;
namespace OpenCodeManager.Telegram;

public sealed partial class TelegramInterface
{
    private async Task<bool> BrowserCallbackAsync(string action, string argument, int messageId, CancellationToken ct)
    {
        guard.Demand(security.OwnerUserId);
        switch (action)
        {
            case "files": await BrowserRootsAsync(messageId, ct); return true;
            case "froot":
                if (!Guid.TryParseExact(argument, "N", out var rootId)) return true;
                var root = (await directories.RootsAsync(ct)).SingleOrDefault(x => x.Id == rootId); if (root is null) return true;
                await BrowseAsync(root.CanonicalPath, 0, messageId, ct); return true;
            case "fdir":
                if (!choices.TryGetValue(argument, out var choice) || choice.Kind != "directory" || choice.Expires < DateTime.UtcNow) { await BrowserRootsAsync(messageId, ct); return true; }
                await BrowseAsync(choice.Value, 0, messageId, ct); return true;
            case "fpage":
                var pageNav = await manager.NavigationAsync(security.OwnerUserId, ct); if (pageNav.BrowserPath is not null) await BrowseAsync(pageNav.BrowserPath, Page(argument), messageId, ct); return true;
            case "fback":
                var backNav = await manager.NavigationAsync(security.OwnerUserId, ct);
                if (backNav.BrowserPath is not null)
                {
                    var listing = await browser.GetDirectoriesAsync(backNav.BrowserPath, cancellationToken: ct);
                    if (listing.ParentPath is not null) { await BrowseAsync(listing.ParentPath, 0, messageId, ct); return true; }
                }
                await BrowserRootsAsync(messageId, ct); return true;
            case "fopen": await OpenBrowserDirectoryAsync(messageId, ct); return true;
            case "ffavorite":
                var favoriteNav = await manager.NavigationAsync(security.OwnerUserId, ct); if (favoriteNav.BrowserPath is not null) await directories.ToggleFavoriteAsync(favoriteNav.BrowserPath, security.OwnerUserId, ct);
                await OpenBrowserDirectoryAsync(messageId, ct); return true;
            case "ffavorites": await BrowserBookmarksAsync(true, Page(argument), messageId, ct); return true;
            case "frecent": await BrowserBookmarksAsync(false, Page(argument), messageId, ct); return true;
            case "fhidden":
                var hiddenNav = await manager.NavigationAsync(security.OwnerUserId, ct); hiddenNav.ShowHiddenDirectories = !hiddenNav.ShowHiddenDirectories; await manager.SaveNavigationAsync(hiddenNav, ct);
                if (hiddenNav.BrowserPath is not null) await BrowseAsync(hiddenNav.BrowserPath,0,messageId,ct); else await BrowserRootsAsync(messageId,ct); return true;
            case "fgit":
                var gitNav = await manager.NavigationAsync(security.OwnerUserId,ct); if (gitNav.BrowserPath is not null) await SendTextAsync(await browser.GitStatusAsync(gitNav.BrowserPath,ct),null,ct); return true;
            case "fsessions":
                var sessionNav = await manager.NavigationAsync(security.OwnerUserId,ct); if (sessionNav.BrowserPath is null) return true;
                var details = await browser.OpenAsync(sessionNav.BrowserPath,security.OwnerUserId,ct);
                await using (var db = await factory.CreateAsync(ct))
                {
                    var sessions = await db.Sessions.Where(x => x.ProjectId == details.ProjectId && !x.Hidden).OrderBy(x => x.Title).Skip(Page(argument)*8).Take(9).ToListAsync(ct);
                    var rows = sessions.Take(8).Select(x => new[] { B(x.Title[..Math.Min(55,x.Title.Length)],"session",x.Id.ToString("N")) }).ToList();
                    if (Page(argument)>0) rows.Add([B("←","fsessions",(Page(argument)-1).ToString())]); if (sessions.Count>8) rows.Add([B("→","fsessions",(Page(argument)+1).ToString())]);
                    rows.Add([B("Новая сессия","newsession",details.ProjectId.ToString("N")),B("Назад","fopen","0")]); await RenderBrowserAsync("Сессии директории\n"+details.CanonicalPath,new(rows),messageId,ct);
                }
                return true;
            default: return false;
        }
    }
    private async Task BrowserRootsAsync(int? messageId,CancellationToken ct)
    {
        var roots = await directories.RootsAsync(ct); var nav=await manager.NavigationAsync(security.OwnerUserId,ct); nav.BrowserPath=null; await manager.SaveNavigationAsync(nav,ct);
        var rows=roots.Select(x=>new[]{B("📁 "+x.Name,"froot",x.Id.ToString("N"))}).ToList();
        rows.Add([B("Избранное","ffavorites","0"),B("Недавние","frecent","0")]); rows.Add([B(nav.ShowHiddenDirectories?"Скрытые: да":"Скрытые: нет","fhidden","0"),B("Меню","home","0")]);
        await RenderBrowserAsync("Файловый менеджер\nРабота только внутри зарегистрированных корней.",new(rows),messageId,ct);
    }
    private async Task BrowseAsync(string path,int page,int? messageId,CancellationToken ct)
    {
        var nav=await manager.NavigationAsync(security.OwnerUserId,ct); var listing=await browser.GetDirectoriesAsync(path,page,nav.ShowHiddenDirectories,ct);
        nav.BrowserPath=listing.CanonicalPath; await manager.SaveNavigationAsync(nav,ct);
        var rows=listing.Directories.Select(x=>new[]{B("📁 "+x.Name+(x.IsSymbolicLink?" ↗":""),"fdir",StoreChoice("directory",x.CanonicalPath,null,null))}).ToList();
        var paging=new List<InlineKeyboardButton>(); if(page>0)paging.Add(B("←","fpage",(page-1).ToString())); if(listing.HasMore)paging.Add(B("→","fpage",(page+1).ToString())); if(paging.Count>0)rows.Add(paging.ToArray());
        if(listing.Error is null) rows.Add([B("Открыть эту папку","fopen","0")]);
        rows.Add([B("Назад","fback","0"),B("В корень","files","0")]); rows.Add([B(nav.ShowHiddenDirectories?"Скрытые: да":"Скрытые: нет","fhidden","0")]);
        await RenderBrowserAsync("Путь:\n"+listing.CanonicalPath+"\n"+(listing.Error??"Страница "+(page+1)),new(rows),messageId,ct);
    }
    private async Task OpenBrowserDirectoryAsync(int? messageId,CancellationToken ct)
    {
        var nav=await manager.NavigationAsync(security.OwnerUserId,ct); if(nav.BrowserPath is null){await BrowserRootsAsync(messageId,ct);return;}
        var details=await browser.OpenAsync(nav.BrowserPath,security.OwnerUserId,ct);nav.SelectedProjectId=details.ProjectId;await manager.SaveNavigationAsync(nav,ct);
        var rows=new List<InlineKeyboardButton[]> { new[]{B("Существующие сессии","fsessions","0"),B("Новая сессия","newsession",details.ProjectId.ToString("N"))},new[]{B("Git Status","fgit","0"),B("★ Избранное","ffavorite","0")},new[]{B("Назад к папкам","fpage","0"),B("В корень","files","0")} };
        await RenderBrowserAsync($"Директория:\n{details.CanonicalPath}\n\nТип: {details.Technology}\nGit: {(details.IsGitRepository?"Repository":"нет")}\nСессий OpenCode: {details.Sessions}",new(rows),messageId,ct);
    }
    private async Task BrowserBookmarksAsync(bool favorites,int page,int? messageId,CancellationToken ct)
    {
        var list=(await directories.BookmarksAsync(favorites,security.OwnerUserId,ct)).Skip(page*8).Take(9).ToList();
        var rows=list.Take(8).Select(x=>new[]{B("📁 "+x.CanonicalPath,"fdir",StoreChoice("directory",x.CanonicalPath,null,null))}).ToList();
        var action=favorites?"ffavorites":"frecent";if(page>0)rows.Add([B("←",action,(page-1).ToString())]);if(list.Count>8)rows.Add([B("→",action,(page+1).ToString())]);rows.Add([B("Корни","files","0")]);
        await RenderBrowserAsync(favorites?"Избранное":"Недавние",new(rows),messageId,ct);
    }
    private async Task RenderBrowserAsync(string text,InlineKeyboardMarkup keyboard,int? messageId,CancellationToken ct)
    {
        var nav=await manager.NavigationAsync(security.OwnerUserId,ct);var id=messageId??nav.BrowserMessageId;
        text=redactor.Redact(text);if(text.Length>3900)text=text[..3900];
        await outgoing.WaitAsync(ct);
        try
        {
            if(id is int existing)
            {
                try { await bot.EditMessageText(Chat,existing,text,replyMarkup:keyboard,cancellationToken:ct); }
                catch(ApiRequestException e) when(e.ErrorCode==400 && e.Message.Contains("message is not modified",StringComparison.OrdinalIgnoreCase)) { }
                catch(ApiRequestException e) when(e.ErrorCode==400 && (e.Message.Contains("message to edit not found",StringComparison.OrdinalIgnoreCase)||e.Message.Contains("message can't be edited",StringComparison.OrdinalIgnoreCase))) { id=null; }
            }
            if(id is null)id=(await bot.SendMessage(Chat,text,replyMarkup:keyboard,cancellationToken:ct)).Id;
            nav.BrowserMessageId=id;await manager.SaveNavigationAsync(nav,ct);await Task.Delay(1100,ct);
        }
        finally{outgoing.Release();}
    }
}
