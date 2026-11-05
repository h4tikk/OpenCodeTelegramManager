namespace OpenCodeManager.Domain;
public enum AgentTaskStatus
{
    Queued,
    Dispatching,
    Running,
    AwaitingApproval,
    Completed,
    Failed,
    Cancelled,
    NeedsReconciliation
}
