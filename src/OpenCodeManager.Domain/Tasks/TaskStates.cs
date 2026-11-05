namespace OpenCodeManager.Domain;
public static class TaskStates
{
    public static bool IsActive(AgentTaskStatus s) => s is AgentTaskStatus.Dispatching or AgentTaskStatus.Running or AgentTaskStatus.AwaitingApproval or AgentTaskStatus.NeedsReconciliation;
    public static bool IsTerminal(AgentTaskStatus s) => s is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled;
    public static bool CanTransition(AgentTaskStatus from, AgentTaskStatus to) => from == to || (from, to) switch
    {
        (AgentTaskStatus.Queued, AgentTaskStatus.Dispatching or AgentTaskStatus.Cancelled) => true,
        (AgentTaskStatus.Dispatching, AgentTaskStatus.Running or AgentTaskStatus.Failed or AgentTaskStatus.NeedsReconciliation) => true,
        (AgentTaskStatus.Running or AgentTaskStatus.AwaitingApproval or AgentTaskStatus.NeedsReconciliation, AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled or AgentTaskStatus.AwaitingApproval or AgentTaskStatus.Running or AgentTaskStatus.NeedsReconciliation) => true,
        _ => false
    };
}
