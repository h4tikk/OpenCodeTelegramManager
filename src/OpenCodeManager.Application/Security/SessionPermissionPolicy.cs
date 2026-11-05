namespace OpenCodeManager.Application;
public static class SessionPermissionPolicy
{
    public static List<PermissionRule> CreateRules() => [new("*", "*", "ask"), new("external_directory", "*", "deny"), new("read", "*.env", "deny"), new("read", "*.env.*", "deny"), new("read", "*/.ssh/*", "deny"), new("read", "*/.gnupg/*", "deny"), new("edit", "*.env*", "deny")];
}
