namespace OpenCodeManager.Domain;
public sealed class Project : Entity
{
    public string Name { get; set; } = "";
    public string AbsolutePath { get; set; } = "";
    public string? Technology { get; set; }
    public ProjectKind Kind { get; set; }
    public string? OpenCodeProjectId { get; set; }
    public Guid? ParentProjectId { get; set; }
    public Project? Parent { get; set; }
    public List<Project> Children { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
