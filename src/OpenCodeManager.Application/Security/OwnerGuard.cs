namespace OpenCodeManager.Application;
public sealed class OwnerGuard(BotSecurityOptions options)
{
    public bool IsAuthorized(long userId, long chatId, bool isPrivate) => options.OwnerUserId > 0 && userId == options.OwnerUserId && isPrivate && chatId == (options.OwnerChatId > 0 ? options.OwnerChatId : options.OwnerUserId);
    public void Demand(long userId)
    {
        if (options.OwnerUserId <= 0 || userId != options.OwnerUserId)
            throw new UnauthorizedAccessException("Owner authorization required");
    }
}
