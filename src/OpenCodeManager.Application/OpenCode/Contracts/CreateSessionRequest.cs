namespace OpenCodeManager.Application;
public sealed record CreateSessionRequest(string Id, string Title, LocationRef Location, List<PermissionRule> Permissions);
