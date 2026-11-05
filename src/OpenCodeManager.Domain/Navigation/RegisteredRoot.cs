namespace OpenCodeManager.Domain;
public sealed class RegisteredRoot : Entity
{
    public string Name { get; set; } = "";
    public string CanonicalPath { get; set; } = "";
}
