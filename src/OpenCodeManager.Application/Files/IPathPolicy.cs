using OpenCodeManager.Domain;

namespace OpenCodeManager.Application;
public interface IPathPolicy
{
    string Canonicalize(string path);
    string DemandAllowed(string path);
    string LockKey(string path);
    IEnumerable<(string Path, string? Parent, ProjectKind Kind)> Discover();
}
