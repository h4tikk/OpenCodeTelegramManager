using OpenCodeManager.Domain;

namespace OpenCodeManager.Telegram;
public static class TaskMessageFormatter
{
    public static string TaskText(AgentTask t, AgentSession s) => $"Задача #{t.Id.ToString("N")[..8]}\n\nСессия: {s.Title}\nМодель: {t.ProviderId}/{t.ModelId}\nАгент: {t.AgentName}\nСтатус: {t.Status}\nДлительность: {((t.CompletedAtUtc ?? DateTime.UtcNow) - (t.StartedAtUtc ?? t.CreatedAtUtc)):hh\\:mm\\:ss}\n\n{t.CurrentAction}\n{t.LastError}\n{(TaskStates.IsTerminal(t.Status) ? t.ResultPreview : "")}";
}
