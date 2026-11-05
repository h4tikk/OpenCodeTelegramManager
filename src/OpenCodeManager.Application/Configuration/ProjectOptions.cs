namespace OpenCodeManager.Application;
public sealed class ProjectOptions
{
    public string[] AllowedRoots { get; set; } = new[]
    {
        "Projects",
        "Development",
        "Code",
        "Workspace",
        "University"
    }.Select(x => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), x)).Where(Directory.Exists).ToArray();
    public int DiscoveryDepth { get; set; } = 3;
    public int MaxDiscoveredDirectories { get; set; } = 1500;
}
