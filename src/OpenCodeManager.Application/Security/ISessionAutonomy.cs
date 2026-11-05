namespace OpenCodeManager.Application;
public interface ISessionAutonomy
{
    bool Enabled { get; }

    Task ConfigureAsync(OpenCodeSession session, string directory, CancellationToken ct);
    Task ConfigureActiveTasksAsync(CancellationToken ct);
}
