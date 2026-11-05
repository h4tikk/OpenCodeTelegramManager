namespace OpenCodeManager.Application;
public sealed class TaskExecutionGate()
{
    public SemaphoreSlim Gate { get; } = new(1);
}
