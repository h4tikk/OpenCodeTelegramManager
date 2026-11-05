namespace OpenCodeManager.Application;
public sealed class TaskExecutionOptions
{
    public int MaxConcurrentTasks { get; set; } = 2;
    public int MaxDurationMinutes { get; set; } = 60;
    public int MaxToolCalls { get; set; } = 200;
    public int MaxVerificationCycles { get; set; } = 8;
    public int PollSeconds { get; set; } = 3;
    public int MaxEventsPerAttempt { get; set; } = 300;
}
