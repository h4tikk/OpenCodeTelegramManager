using OpenCodeManager.Application;

namespace OpenCodeManager.Telegram;
public sealed class TelegramConversationHandler(TelegramUi ui, NavigationService navigation, ProjectManager projects, SessionManager sessions, ProjectMenuHandler projectScreens, SessionMenuHandler sessionScreens, TaskMenuHandler taskScreens, FileMenuHandler fileMenus) : TelegramHandler(ui)
{
    public async Task HandleAsync(string input, int updateId, CancellationToken ct)
    {
        var nav = await navigation.NavigationAsync(security.OwnerUserId, ct);
        if (await fileMenus.HandleFileInputAsync(nav, input, ct))
            return;
        if (nav.InputMode == "sessiontitle" && nav.SelectedProjectId is Guid projectId)
        {
            var s = await sessions.CreateSessionAsync(projectId, input, nav.SelectedSessionId, security.OwnerUserId, ct);
            nav.SelectedSessionId = s.Id;
            nav.InputMode = null;
            await navigation.SaveNavigationAsync(nav, ct);
            await sessionScreens.SessionAsync(s.Id, ct);
            return;
        }

        if (nav.InputMode == "rename" && nav.SelectedSessionId is Guid renameId)
        {
            await sessions.RenameAsync(renameId, input, security.OwnerUserId, ct);
            nav.InputMode = null;
            await navigation.SaveNavigationAsync(nav, ct);
            await sessionScreens.SessionAsync(renameId, ct);
            return;
        }

        if (nav.InputMode == "addproject")
        {
            await projects.AddProjectAsync(input, security.OwnerUserId, ct);
            nav.InputMode = null;
            await navigation.SaveNavigationAsync(nav, ct);
            await projectScreens.ProjectsAsync(0, ct);
            return;
        }

        await taskScreens.SubmitAsync(nav, input, updateId, ct);
    }
}
